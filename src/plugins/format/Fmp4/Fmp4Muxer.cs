using System.Runtime.CompilerServices;
using Shared.Models;
using Shared.Models.Formats;

namespace Format.Fmp4;

public enum MuxerCodec
{
  H264,
  H265
}

public sealed class Fmp4Muxer
{
  private readonly MuxerCodec _codec;
  private readonly IDataStream _input;
  private readonly TimestampConverter _timestamps;
  private readonly FragmentAssembler _assembler;
  private readonly Action<KeyframeOffset>? _onKeyframe;

  private byte[]? _initSegment;
  private byte[]? _currentSps;
  private byte[]? _currentPps;
  private byte[]? _currentVps;
  private uint _lastDuration;

  public Fmp4Muxer(
    MuxerCodec codec,
    IDataStream input,
    TimestampConverter timestamps,
    Action<KeyframeOffset>? onKeyframe = null)
  {
    _codec = codec;
    _input = input;
    _timestamps = timestamps;
    _assembler = new FragmentAssembler(timestamps);
    _onKeyframe = onKeyframe;
  }

  public byte[]? InitSegment => _initSegment;

  public async Task<VideoStreamInfo> InitAsync(int fps, CancellationToken ct)
  {
    if (_codec == MuxerCodec.H264)
    {
      var typed = (IDataStream<H264NalUnit>)_input;
      await foreach (var nal in typed.ReadAsync(ct))
      {
        var rawNal = NalConverter.StripStartCode(nal.Data.Span);
        if (nal.NalType == H264NalType.Sps)
          _currentSps = rawNal.ToArray();
        else if (nal.NalType == H264NalType.Pps)
          _currentPps = rawNal.ToArray();

        if (_currentSps != null && _currentPps != null)
          return BuildInitSegment(_currentSps, _currentPps, [], fps).Info;
      }
    }
    else
    {
      var typed = (IDataStream<H265NalUnit>)_input;
      await foreach (var nal in typed.ReadAsync(ct))
      {
        var rawNal = NalConverter.StripStartCode(nal.Data.Span);
        if (nal.NalType == H265NalType.Vps)
          _currentVps = rawNal.ToArray();
        else if (nal.NalType == H265NalType.Sps)
          _currentSps = rawNal.ToArray();
        else if (nal.NalType == H265NalType.Pps)
          _currentPps = rawNal.ToArray();

        if (_currentVps != null && _currentSps != null && _currentPps != null)
          return BuildInitSegment(_currentSps, _currentPps, _currentVps, fps).Info;
      }
    }

    throw new OperationCanceledException("Stream ended before SPS/PPS received");
  }

  public (byte[] Segment, VideoStreamInfo Info) BuildInitSegment(
    ReadOnlySpan<byte> sps, ReadOnlySpan<byte> pps, ReadOnlySpan<byte> vps, int fps = 0)
  {
    var ftyp = FtypBuilder.Build();
    byte[] moov;
    string mimeType;
    int width, height;

    if (_codec == MuxerCodec.H264)
    {
      var spsInfo = H264SpsParser.Parse(sps);
      var avcC = AvcCBuilder.Build(sps, pps, spsInfo);
      moov = MoovBuilder.BuildH264(spsInfo.Width, spsInfo.Height, _timestamps.Timescale, avcC);
      mimeType = $"video/mp4; codecs=\"avc1.{spsInfo.ProfileIdc:X2}{spsInfo.ProfileCompatibility:X2}{spsInfo.LevelIdc:X2}\"";
      width = spsInfo.Width;
      height = spsInfo.Height;
    }
    else
    {
      var vpsInfo = H265SpsParser.ParseVps(vps);
      var spsInfo = H265SpsParser.ParseSps(sps);
      var hvcC = HvcCBuilder.Build(vps, sps, pps, vpsInfo, spsInfo);
      moov = MoovBuilder.BuildH265(spsInfo.Width, spsInfo.Height, _timestamps.Timescale, hvcC);
      var ptl = spsInfo.Ptl;
      var tier = ptl.GeneralTierFlag ? 'H' : 'L';
      mimeType = $"video/mp4; codecs=\"hev1.{ptl.GeneralProfileIdc}.{ptl.GeneralProfileCompatibilityFlags:X}.{tier}{ptl.GeneralLevelIdc}\"";
      width = spsInfo.Width;
      height = spsInfo.Height;
    }

    var init = new byte[ftyp.Length + moov.Length];
    ftyp.CopyTo(init, 0);
    moov.CopyTo(init, ftyp.Length);

    _initSegment = init;
    _assembler.AddHeaderBytes(init.Length);

    var info = new VideoStreamInfo
    {
      DataFormat = "fmp4",
      MimeType = mimeType,
      Resolution = $"{width}x{height}",
      Fps = fps
    };

    return (init, info);
  }

  public bool TryUpdateParameters(ReadOnlySpan<byte> sps, ReadOnlySpan<byte> pps, ReadOnlySpan<byte> vps)
  {
    if (_currentSps != null && sps.SequenceEqual(_currentSps)
      && _currentPps != null && pps.SequenceEqual(_currentPps))
    {
      if (_codec == MuxerCodec.H264)
        return false;
      if (_currentVps != null && vps.SequenceEqual(_currentVps))
        return false;
    }

    _currentSps = sps.ToArray();
    _currentPps = pps.ToArray();
    if (_codec == MuxerCodec.H265)
      _currentVps = vps.ToArray();

    return true;
  }

  public async IAsyncEnumerable<Fmp4Fragment> MuxAsync(
    [EnumeratorCancellation] CancellationToken ct)
  {
    if (_codec == MuxerCodec.H264)
    {
      var typed = (IDataStream<H264NalUnit>)_input;
      await foreach (var fragment in MuxH264Async(typed, ct))
        yield return fragment;
    }
    else
    {
      var typed = (IDataStream<H265NalUnit>)_input;
      await foreach (var fragment in MuxH265Async(typed, ct))
        yield return fragment;
    }
  }

  private async IAsyncEnumerable<Fmp4Fragment> MuxH264Async(
    IDataStream<H264NalUnit> input,
    [EnumeratorCancellation] CancellationToken ct)
  {
    byte[]? pendingSps = null;
    byte[]? pendingPps = null;
    var accessUnit = new List<H264NalUnit>();
    ulong? currentTimestamp = null;

    await foreach (var nal in input.ReadAsync(ct))
    {
      var rawNal = NalConverter.StripStartCode(nal.Data.Span);

      if (nal.NalType == H264NalType.Sps)
      {
        pendingSps = rawNal.ToArray();
        continue;
      }
      if (nal.NalType == H264NalType.Pps)
      {
        pendingPps = rawNal.ToArray();
        continue;
      }
      if (nal.NalType is H264NalType.Sei or H264NalType.Other)
        continue;

      if (nal.IsSyncPoint && pendingSps != null && pendingPps != null)
      {
        if (TryUpdateParameters(pendingSps, pendingPps, []))
        {
          var (init, _) = BuildInitSegment(pendingSps, pendingPps, []);
          yield return new Fmp4Fragment
          {
            Data = init,
            Timestamp = nal.Timestamp,
            IsSyncPoint = false,
            IsHeader = true
          };
        }
      }

      if (currentTimestamp != null && nal.Timestamp != currentTimestamp)
      {
        var fragment = EmitAccessUnit(accessUnit, currentTimestamp.Value, nal.Timestamp);
        if (fragment != null)
          yield return fragment;
        accessUnit.Clear();
      }

      accessUnit.Add(nal);
      currentTimestamp = nal.Timestamp;
    }

    if (accessUnit.Count > 0 && currentTimestamp != null)
    {
      var fragment = EmitAccessUnit(accessUnit, currentTimestamp.Value, null);
      if (fragment != null)
        yield return fragment;
    }
  }

  private async IAsyncEnumerable<Fmp4Fragment> MuxH265Async(
    IDataStream<H265NalUnit> input,
    [EnumeratorCancellation] CancellationToken ct)
  {
    byte[]? pendingVps = null;
    byte[]? pendingSps = null;
    byte[]? pendingPps = null;
    var accessUnit = new List<H265NalUnit>();
    ulong? currentTimestamp = null;

    await foreach (var nal in input.ReadAsync(ct))
    {
      var rawNal = NalConverter.StripStartCode(nal.Data.Span);

      if (nal.NalType == H265NalType.Vps)
      {
        pendingVps = rawNal.ToArray();
        continue;
      }
      if (nal.NalType == H265NalType.Sps)
      {
        pendingSps = rawNal.ToArray();
        continue;
      }
      if (nal.NalType == H265NalType.Pps)
      {
        pendingPps = rawNal.ToArray();
        continue;
      }
      if (nal.NalType is H265NalType.Sei or H265NalType.Other)
        continue;

      if (nal.IsSyncPoint && pendingVps != null && pendingSps != null && pendingPps != null)
      {
        if (TryUpdateParameters(pendingSps, pendingPps, pendingVps))
        {
          var (init, _) = BuildInitSegment(pendingSps, pendingPps, pendingVps);
          yield return new Fmp4Fragment
          {
            Data = init,
            Timestamp = nal.Timestamp,
            IsSyncPoint = false,
            IsHeader = true
          };
        }
      }

      if (currentTimestamp != null && nal.Timestamp != currentTimestamp)
      {
        var fragment = EmitAccessUnit(accessUnit, currentTimestamp.Value, nal.Timestamp);
        if (fragment != null)
          yield return fragment;
        accessUnit.Clear();
      }

      accessUnit.Add(nal);
      currentTimestamp = nal.Timestamp;
    }

    if (accessUnit.Count > 0 && currentTimestamp != null)
    {
      var fragment = EmitAccessUnit(accessUnit, currentTimestamp.Value, null);
      if (fragment != null)
        yield return fragment;
    }
  }

  private Fmp4Fragment? EmitAccessUnit<T>(List<T> accessUnit, ulong timestamp, ulong? nextTimestamp) where T : IDataUnit
  {
    if (accessUnit.Count == 0 || _initSegment == null)
      return null;

    var isKeyframe = accessUnit[0].IsSyncPoint;
    var nalData = new List<ReadOnlyMemory<byte>>(accessUnit.Count);
    var totalNalSize = 0;

    foreach (var nal in accessUnit)
    {
      nalData.Add(nal.Data);
      totalNalSize += NalConverter.LengthPrefixedSize(nal.Data.Span);
    }

    var duration = nextTimestamp != null
      ? _timestamps.DurationBetween(timestamp, nextTimestamp.Value)
      : _lastDuration > 0 ? _lastDuration : _timestamps.Timescale / 30;
    _lastDuration = duration;

    var samples = new List<SampleEntry>
    {
      new()
      {
        Duration = duration,
        Size = totalNalSize,
        IsKeyframe = isKeyframe,
        CompositionOffset = 0
      }
    };

    var (fragment, keyframe) = _assembler.Assemble(nalData, samples, timestamp, isKeyframe);
    if (keyframe != null)
      _onKeyframe?.Invoke(keyframe);

    return fragment;
  }
}
