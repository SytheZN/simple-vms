using Shared.Models;
using Shared.Models.Formats;

namespace Capture.Rtsp;

public interface IRtpDepacketizer
{
  IDataUnit? ProcessPacket(ReadOnlySpan<byte> rtpPayload, ulong timestamp);
}

public sealed class RtpH264Depacketizer : IRtpDepacketizer
{
  private static readonly byte[] StartCode = [0x00, 0x00, 0x00, 0x01];

  private MemoryStream? _fuBuffer;
  private ulong _fuTimestamp;
  private byte _fuNri;
  private byte _fuType;

  public IDataUnit? ProcessPacket(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    if (rtpPayload.Length < 1)
      return null;

    var header = rtpPayload[0];
    var nalType = header & 0x1F;

    return nalType switch
    {
      >= 1 and <= 23 => ProcessSingleNal(rtpPayload, timestamp),
      24 => ProcessStapA(rtpPayload, timestamp),
      28 => ProcessFuA(rtpPayload, timestamp),
      _ => null
    };
  }

  public IReadOnlyList<IDataUnit> ProcessStapAAll(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    if (rtpPayload.Length < 2)
      return [];

    var results = new List<IDataUnit>();
    var offset = 1;

    while (offset + 2 <= rtpPayload.Length)
    {
      var nalSize = (rtpPayload[offset] << 8) | rtpPayload[offset + 1];
      offset += 2;

      if (offset + nalSize > rtpPayload.Length)
        break;

      var nalData = BuildAnnexB(rtpPayload.Slice(offset, nalSize));
      var nal = CreateH264NalUnit(nalData, timestamp);
      results.Add(nal);
      offset += nalSize;
    }

    return results;
  }

  private IDataUnit? ProcessSingleNal(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    var nalData = BuildAnnexB(rtpPayload);
    return CreateH264NalUnit(nalData, timestamp);
  }

  private IDataUnit? ProcessStapA(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    var all = ProcessStapAAll(rtpPayload, timestamp);
    return all.Count > 0 ? all[0] : null;
  }

  private IDataUnit? ProcessFuA(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    if (rtpPayload.Length < 2)
      return null;

    var fuIndicator = rtpPayload[0];
    var fuHeader = rtpPayload[1];
    var start = (fuHeader & 0x80) != 0;
    var end = (fuHeader & 0x40) != 0;
    var nalType = fuHeader & 0x1F;
    var nri = fuIndicator & 0x60;

    if (start)
    {
      _fuBuffer?.Dispose();
      _fuBuffer = new MemoryStream();
      _fuTimestamp = timestamp;
      _fuNri = (byte)nri;
      _fuType = (byte)nalType;
      _fuBuffer.Write(rtpPayload[2..]);
    }
    else if (_fuBuffer != null)
    {
      _fuBuffer.Write(rtpPayload[2..]);
    }
    else
    {
      return null;
    }

    if (end && _fuBuffer != null)
    {
      var reconstructedHeader = (byte)(_fuNri | _fuType);
      var fragmentData = _fuBuffer.ToArray();
      _fuBuffer.Dispose();
      _fuBuffer = null;

      var nalData = new byte[StartCode.Length + 1 + fragmentData.Length];
      StartCode.CopyTo(nalData, 0);
      nalData[StartCode.Length] = reconstructedHeader;
      fragmentData.CopyTo(nalData, StartCode.Length + 1);

      return CreateH264NalUnit(nalData, _fuTimestamp);
    }

    return null;
  }

  private static byte[] BuildAnnexB(ReadOnlySpan<byte> nalUnit)
  {
    var result = new byte[StartCode.Length + nalUnit.Length];
    StartCode.CopyTo(result, 0);
    nalUnit.CopyTo(result.AsSpan(StartCode.Length));
    return result;
  }

  internal static H264NalUnit CreateH264NalUnit(byte[] annexBData, ulong timestamp)
  {
    var nalType = ClassifyH264(annexBData.AsSpan(4, 1)[0]);
    return new H264NalUnit
    {
      Data = annexBData,
      Timestamp = timestamp,
      IsSyncPoint = nalType == H264NalType.Idr,
      NalType = nalType
    };
  }

  internal static H264NalType ClassifyH264(byte nalByte)
  {
    var type = nalByte & 0x1F;
    return type switch
    {
      1 => H264NalType.Slice,
      5 => H264NalType.Idr,
      6 => H264NalType.Sei,
      7 => H264NalType.Sps,
      8 => H264NalType.Pps,
      _ => H264NalType.Other
    };
  }
}

public sealed class RtpH265Depacketizer : IRtpDepacketizer
{
  private static readonly byte[] StartCode = [0x00, 0x00, 0x00, 0x01];

  private MemoryStream? _fuBuffer;
  private ulong _fuTimestamp;
  private byte _fuType;
  private byte _fuLayerTid;

  public IDataUnit? ProcessPacket(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    if (rtpPayload.Length < 2)
      return null;

    var nalType = (rtpPayload[0] >> 1) & 0x3F;

    return nalType switch
    {
      >= 0 and <= 47 => ProcessSingleNal(rtpPayload, timestamp),
      48 => ProcessAp(rtpPayload, timestamp),
      49 => ProcessFu(rtpPayload, timestamp),
      _ => null
    };
  }

  public IReadOnlyList<IDataUnit> ProcessApAll(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    if (rtpPayload.Length < 4)
      return [];

    var results = new List<IDataUnit>();
    var offset = 2;

    while (offset + 2 <= rtpPayload.Length)
    {
      var nalSize = (rtpPayload[offset] << 8) | rtpPayload[offset + 1];
      offset += 2;

      if (offset + nalSize > rtpPayload.Length)
        break;

      var nalData = BuildAnnexB(rtpPayload.Slice(offset, nalSize));
      results.Add(CreateH265NalUnit(nalData, timestamp));
      offset += nalSize;
    }

    return results;
  }

  private IDataUnit? ProcessSingleNal(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    var nalData = BuildAnnexB(rtpPayload);
    return CreateH265NalUnit(nalData, timestamp);
  }

  private IDataUnit? ProcessAp(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    var all = ProcessApAll(rtpPayload, timestamp);
    return all.Count > 0 ? all[0] : null;
  }

  private IDataUnit? ProcessFu(ReadOnlySpan<byte> rtpPayload, ulong timestamp)
  {
    if (rtpPayload.Length < 3)
      return null;

    var payloadHeader0 = rtpPayload[0];
    var payloadHeader1 = rtpPayload[1];
    var fuHeader = rtpPayload[2];
    var start = (fuHeader & 0x80) != 0;
    var end = (fuHeader & 0x40) != 0;
    var nalType = fuHeader & 0x3F;

    if (start)
    {
      _fuBuffer?.Dispose();
      _fuBuffer = new MemoryStream();
      _fuTimestamp = timestamp;
      _fuType = (byte)nalType;
      _fuLayerTid = payloadHeader1;
      _fuBuffer.Write(rtpPayload[3..]);
    }
    else if (_fuBuffer != null)
    {
      _fuBuffer.Write(rtpPayload[3..]);
    }
    else
    {
      return null;
    }

    if (end && _fuBuffer != null)
    {
      var reconstructedHeader0 = (byte)((_fuType << 1) | (payloadHeader0 & 0x81));
      var fragmentData = _fuBuffer.ToArray();
      _fuBuffer.Dispose();
      _fuBuffer = null;

      var nalData = new byte[StartCode.Length + 2 + fragmentData.Length];
      StartCode.CopyTo(nalData, 0);
      nalData[StartCode.Length] = reconstructedHeader0;
      nalData[StartCode.Length + 1] = _fuLayerTid;
      fragmentData.CopyTo(nalData, StartCode.Length + 2);

      return CreateH265NalUnit(nalData, _fuTimestamp);
    }

    return null;
  }

  private static byte[] BuildAnnexB(ReadOnlySpan<byte> nalUnit)
  {
    var result = new byte[StartCode.Length + nalUnit.Length];
    StartCode.CopyTo(result, 0);
    nalUnit.CopyTo(result.AsSpan(StartCode.Length));
    return result;
  }

  internal static H265NalUnit CreateH265NalUnit(byte[] annexBData, ulong timestamp)
  {
    var nalType = ClassifyH265(annexBData.AsSpan(4, 1)[0]);
    return new H265NalUnit
    {
      Data = annexBData,
      Timestamp = timestamp,
      IsSyncPoint = nalType is H265NalType.IdrWRadl or H265NalType.IdrNLp,
      NalType = nalType
    };
  }

  internal static H265NalType ClassifyH265(byte nalByte)
  {
    var type = (nalByte >> 1) & 0x3F;
    return type switch
    {
      0 => H265NalType.TrailN,
      1 => H265NalType.TrailR,
      19 => H265NalType.IdrWRadl,
      20 => H265NalType.IdrNLp,
      32 => H265NalType.Vps,
      33 => H265NalType.Sps,
      34 => H265NalType.Pps,
      39 or 40 => H265NalType.Sei,
      _ => H265NalType.Other
    };
  }
}
