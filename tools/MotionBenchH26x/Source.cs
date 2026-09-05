using System.Buffers.Binary;
using Format.Fmp4;
using Shared.Models.Formats;

namespace MotionBenchH26x;

internal sealed record Source<TNal>(
  IReadOnlyList<TNal> Feed, int Frames, int Keyframes,
  long FedBytes, double DurationSeconds, long Bytes);

internal static class Source
{
  private const int VisualSampleEntryBytes = 78;
  private const ulong Rtp90kHzClock = 90_000UL;

  private const int AvcCConfigMinBytes = 6;
  private const int AvcCLengthSizeOffset = 4;
  private const int AvcCSpsCountOffset = 5;
  private const byte NalUnitTypeMask = 0x1F;
  private const byte NalTypeIdr = 5;
  private const byte NalTypeSps = 7;
  private const byte NalTypePps = 8;

  private const int HvcCLengthSizeOffset = 21;
  private const int HvcCArraysOffset = 22;
  private const byte IrapNalTypeMax = 21;
  private const byte VclNalTypeMax = 9;
  private const byte NalTypeIdrWRadl = 19;
  private const byte NalTypeIdrNLp = 20;

  public static async Task<object?> Open(string segment)
  {
    var bytes = new FileInfo(segment).Length;

    string entryType;
    List<byte[]> parameterSets;
    int nalLengthSize;
    using (var moovStream = File.OpenRead(segment))
    {
      var parsed = ReadParameterSets(new BoxReader(moovStream));
      if (parsed == null)
      {
        Console.Error.WriteLine($"{segment} holds no moov avcC/hvcC parameter sets");
        return null;
      }
      (entryType, parameterSets, nalLengthSize) = parsed.Value;
    }

    return entryType is "avc1" or "avc3"
      ? await Load(segment, bytes, parameterSets, nalLengthSize, H264Unit, AppendH264Nals)
      : await Load(segment, bytes, parameterSets, nalLengthSize, H265Unit, AppendH265Nals);
  }

  private delegate void Append<TNal>(
    Fmp4Fragment fragment, int nalLengthSize, List<TNal> feed, ref long fedBytes);

  private static async Task<object?> Load<TNal>(
    string segment, long bytes, List<byte[]> parameterSets, int nalLengthSize,
    Func<byte[], ulong, ulong, TNal> unit, Append<TNal> append)
  {
    var feed = new List<TNal>();
    foreach (var parameterSet in parameterSets)
      feed.Add(unit(parameterSet, 0, 0));

    var frames = 0;
    var keyframes = 0;
    long fedBytes = 0;
    var firstMedia = ulong.MaxValue;
    ulong lastMedia = 0;

    await using var reader = new Fmp4SegmentReader(File.OpenRead(segment));
    await foreach (var readUnit in reader.ReadAsync(CancellationToken.None))
    {
      var fragment = (Fmp4Fragment)readUnit;
      firstMedia = Math.Min(firstMedia, fragment.MediaTimestamp);
      lastMedia = Math.Max(lastMedia, fragment.MediaTimestamp);

      if (fragment.IsSyncPoint)
      {
        keyframes++;
        append(fragment, nalLengthSize, feed, ref fedBytes);
        continue;
      }

      var before = feed.Count;
      append(fragment, nalLengthSize, feed, ref fedBytes);
      if (feed.Count > before) frames++;
    }

    if (frames == 0)
    {
      Console.Error.WriteLine($"{segment} holds no p/b frames");
      return null;
    }

    var duration = firstMedia < lastMedia ? (lastMedia - firstMedia) / (double)Rtp90kHzClock : 0;

    return new Source<TNal>(feed, frames, keyframes, fedBytes, duration, bytes);
  }

  private static void AppendH264Nals(
    Fmp4Fragment fragment, int nalLengthSize, List<H264NalUnit> feed, ref long fedBytes)
  {
    var span = fragment.Data.Span;
    var moofSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span);
    var mdatSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span[moofSize..]);
    var end = moofSize + mdatSize;

    for (var at = moofSize + 8; at + nalLengthSize <= end;)
    {
      var length = 0;
      for (var i = 0; i < nalLengthSize; i++)
        length = (length << 8) | span[at + i];
      at += nalLengthSize;

      if (length < 1 || at + length > end)
        throw new InvalidDataException($"mdat NAL length {length} is out of bounds");

      var nalType = (byte)(span[at] & NalUnitTypeMask);
      if (nalType is >= 1 and <= NalTypeIdr)
      {
        feed.Add(H264Unit(
          span.Slice(at, length).ToArray(), fragment.Timestamp, fragment.MediaTimestamp));
        fedBytes += length;
      }

      at += length;
    }
  }

  private static void AppendH265Nals(
    Fmp4Fragment fragment, int nalLengthSize, List<H265NalUnit> feed, ref long fedBytes)
  {
    var span = fragment.Data.Span;
    var moofSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span);
    var mdatSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span[moofSize..]);
    var end = moofSize + mdatSize;

    for (var at = moofSize + 8; at + nalLengthSize <= end;)
    {
      var length = 0;
      for (var i = 0; i < nalLengthSize; i++)
        length = (length << 8) | span[at + i];
      at += nalLengthSize;

      if (length < 2 || at + length > end)
        throw new InvalidDataException($"mdat NAL length {length} is out of bounds");

      var rawType = (byte)((span[at] >> 1) & 0x3F);
      if (rawType <= VclNalTypeMax || rawType is NalTypeIdrWRadl or NalTypeIdrNLp)
      {
        feed.Add(H265Unit(
          span.Slice(at, length).ToArray(), fragment.Timestamp, fragment.MediaTimestamp));
        fedBytes += length;
      }

      at += length;
    }
  }

  private static (string EntryType, List<byte[]> ParameterSets, int NalLengthSize)? ReadParameterSets(
    BoxReader reader)
  {
    var span = DescendTo(reader, ["moov", "trak", "mdia", "minf", "stbl", "stsd"]);
    if (span == null) return null;

    reader.Skip(8);
    var entry = reader.ReadHeader();
    if (entry == null) return null;

    reader.Skip(VisualSampleEntryBytes);
    var entryEnd = entry.DataOffset - entry.HeaderSize + entry.Size;

    var parsed = entry.Type switch
    {
      "avc1" or "avc3" => ReadAvcC(reader, entryEnd),
      "hvc1" or "hev1" => ReadHvcC(reader, entryEnd),
      _ => null
    };
    if (parsed == null) return null;

    return (entry.Type, parsed.Value.ParameterSets, parsed.Value.NalLengthSize);
  }

  private static (List<byte[]> ParameterSets, int NalLengthSize)? ReadAvcC(
    BoxReader reader, long entryEnd)
  {
    var avcC = FindChild(reader, "avcC", entryEnd);
    if (avcC == null) return null;

    var content = reader.ReadBytes((int)(avcC.Size - avcC.HeaderSize));
    if (content == null || content.Length < AvcCConfigMinBytes) return null;

    var nalLengthSize = (content[AvcCLengthSizeOffset] & 3) + 1;
    var parameterSets = new List<byte[]>();

    var at = AvcCSpsCountOffset;
    var spsCount = content[at++] & 0x1F;
    for (var i = 0; i < spsCount; i++)
    {
      var length = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(at));
      at += 2;
      parameterSets.Add(content.AsSpan(at, length).ToArray());
      at += length;
    }

    var ppsCount = content[at++];
    for (var i = 0; i < ppsCount; i++)
    {
      var length = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(at));
      at += 2;
      parameterSets.Add(content.AsSpan(at, length).ToArray());
      at += length;
    }

    return (parameterSets, nalLengthSize);
  }

  private static (List<byte[]> ParameterSets, int NalLengthSize)? ReadHvcC(
    BoxReader reader, long entryEnd)
  {
    var hvcC = FindChild(reader, "hvcC", entryEnd);
    if (hvcC == null) return null;

    var content = reader.ReadBytes((int)(hvcC.Size - hvcC.HeaderSize));
    if (content == null || content.Length < HvcCArraysOffset + 1) return null;

    var nalLengthSize = (content[HvcCLengthSizeOffset] & 3) + 1;
    var parameterSets = new List<byte[]>();

    var at = HvcCArraysOffset;
    var arrays = content[at++];
    for (var i = 0; i < arrays; i++)
    {
      at++;
      var count = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(at));
      at += 2;
      for (var j = 0; j < count; j++)
      {
        var length = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(at));
        at += 2;
        parameterSets.Add(content.AsSpan(at, length).ToArray());
        at += length;
      }
    }

    return (parameterSets, nalLengthSize);
  }

  private static BoxHeader? DescendTo(BoxReader reader, string[] path)
  {
    var end = reader.Length;
    BoxHeader? found = null;
    foreach (var type in path)
    {
      found = FindChild(reader, type, end);
      if (found == null) return null;
      end = found.DataOffset - found.HeaderSize + found.Size;
    }
    return found;
  }

  private static BoxHeader? FindChild(BoxReader reader, string type, long end)
  {
    while (reader.Position < end)
    {
      var header = reader.ReadHeader();
      if (header == null) return null;
      if (header.Type == type) return header;
      reader.Position = header.DataOffset - header.HeaderSize + header.Size;
    }
    return null;
  }

  private static H264NalUnit H264Unit(byte[] data, ulong timestamp, ulong mediaTimestamp)
  {
    var nalType = (byte)(data[0] & NalUnitTypeMask);
    return new H264NalUnit
    {
      Data = data,
      Timestamp = timestamp,
      MediaTimestamp = mediaTimestamp,
      IsSyncPoint = nalType == NalTypeIdr,
      IsHeader = nalType is NalTypeSps or NalTypePps,
      NalType = nalType switch
      {
        NalTypeSps => H264NalType.Sps,
        NalTypePps => H264NalType.Pps,
        NalTypeIdr => H264NalType.Idr,
        >= 1 and <= 5 => H264NalType.Slice,
        _ => H264NalType.Other
      }
    };
  }

  private static H265NalUnit H265Unit(byte[] data, ulong timestamp, ulong mediaTimestamp)
  {
    var type = (data[0] >> 1) & 0x3F;
    return new H265NalUnit
    {
      Data = data,
      Timestamp = timestamp,
      MediaTimestamp = mediaTimestamp,
      IsSyncPoint = type is NalTypeIdrWRadl or NalTypeIdrNLp,
      IsHeader = type > IrapNalTypeMax,
      NalType = type switch
      {
        0 => H265NalType.TrailN,
        NalTypeIdrWRadl => H265NalType.IdrWRadl,
        NalTypeIdrNLp => H265NalType.IdrNLp,
        32 => H265NalType.Vps,
        33 => H265NalType.Sps,
        34 => H265NalType.Pps,
        _ => H265NalType.TrailR
      }
    };
  }
}
