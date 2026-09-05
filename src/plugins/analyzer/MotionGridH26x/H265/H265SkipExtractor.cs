using Microsoft.Extensions.Logging;
using Shared.Models.Formats;
using static Utils.BitstreamHelpers;

namespace Analyzer.MotionGridH26x;

internal sealed class H265SkipExtractor(ILogger logger)
{
  private const int NalHeaderBits = 16;
  private const byte IrapNalTypeMin = 16;
  private const byte IrapNalTypeMax = 23;
  private const int MinNalHeaderBytes = 2;
  private const byte ChromaFormat420 = 1;
  private const ulong MicrosecondsPerSecond = 1_000_000UL;
  private const ulong Rtp90kHzClock = 90_000UL;

  private readonly Dictionary<uint, H265SpsExtended> _sps = [];
  private readonly Dictionary<uint, H265Pps> _pps = [];
  private readonly CabacSliceWalkerH265 _walker = new();
  private readonly MotionVectorField _mvField = new();
  private IObserverHarness<ReconstructionPhase>? _observer;
  private byte[] _rbsp = [];
  private int _rbspLength;

  private H265SpsExtended? _activeSps;
  private byte[]? _frameCells;
  private bool _frameInProgress;
  private bool _frameIntraOnly;
  private bool _frameIsSyncSource;
  private bool _frameValid;
  private ulong _frameTimestamp;
  private ulong _anchorWall;
  private ulong _anchorMedia;
  private bool _hasAnchor;
  private bool _pendingSync;

  private string? _lastRejection;

  public int DroppedFrames { get; private set; }
  public string? LastFailure { get; private set; }
  internal int MinSliceQp { get; private set; } = int.MaxValue;
  internal int MaxSliceQp { get; private set; } = int.MinValue;

  internal void Observe(IObserverHarness<ReconstructionPhase> observer)
  {
    _observer = observer;
    _walker.Observe(observer);
  }

  public bool TryFeed(H265NalUnit nal, out MotionGridUnit? emitted)
  {
    emitted = null;
    var data = nal.Data.Span;
    if (data.Length < MinNalHeaderBytes) return false;

    switch (nal.NalType)
    {
      case H265NalType.Sps:
        var sps = H265SpsExtended.Parse(data);
        if (sps != null) _sps[sps.SpsId] = sps;
        return false;

      case H265NalType.Pps:
        var pps = H265Pps.Parse(data);
        _pps[pps.PpsId] = pps;
        return false;

      case H265NalType.TrailN:
      case H265NalType.TrailR:
        return ProcessSlice(data, nal, out emitted);

      case H265NalType.IdrWRadl:
      case H265NalType.IdrNLp:
        if (_frameInProgress)
        {
          emitted = BuildUnit();
          Reset();
        }
        _mvField.Reset();
        _pendingSync = true;
        _anchorWall = nal.Timestamp;
        _anchorMedia = nal.MediaTimestamp;
        _hasAnchor = true;
        return emitted != null;

      default:
        return false;
    }
  }

  public MotionGridUnit? Flush()
  {
    if (!_frameInProgress) return null;
    var unit = BuildUnit();
    Reset();
    return unit;
  }

  private bool ProcessSlice(ReadOnlySpan<byte> rawNal, H265NalUnit nal, out MotionGridUnit? emitted)
  {
    emitted = null;
    var nalUnitType = (byte)((rawNal[0] >> 1) & 0x3F);

    _observer?.Begin(ReconstructionPhase.Header);
    if (_rbsp.Length < rawNal.Length) _rbsp = new byte[rawNal.Length];
    _rbspLength = ExtractRbsp(rawNal, _rbsp);
    var rbsp = (ReadOnlySpan<byte>)_rbsp[.._rbspLength];

    var bitOffset = 0;
    Skip(ref bitOffset, NalHeaderBits);
    var firstSliceSegment = ReadBit(rbsp, ref bitOffset);
    if (nalUnitType is >= IrapNalTypeMin and <= IrapNalTypeMax)
      Skip(ref bitOffset, 1);
    var ppsId = ReadExpGolomb(rbsp, ref bitOffset);
    if (!_pps.TryGetValue(ppsId, out var pps)) return false;
    if (!_sps.TryGetValue(pps.SpsId, out var sps)) return false;

    if (firstSliceSegment && _frameInProgress)
    {
      emitted = BuildUnit();
      Reset();
    }

    if (!_frameInProgress)
      BeginFrame(nal, sps);

    if (_frameValid && Unsupported(sps, pps) is { } unsupported)
      Invalidate(unsupported);

    var parsed = H265SliceHeader.Parse(rbsp, nalUnitType, firstSliceSegment, bitOffset, sps, pps);
    _observer?.End(ReconstructionPhase.Header);

    if (parsed is not { } header)
    {
      Invalidate("slice header did not parse");
      return emitted != null;
    }
    if (header.DependentSliceSegment)
    {
      Invalidate("dependent slice segments are not supported");
      return emitted != null;
    }

    MinSliceQp = Math.Min(MinSliceQp, header.SliceQpY);
    MaxSliceQp = Math.Max(MaxSliceQp, header.SliceQpY);

    if (!header.IsIntra) _frameIntraOnly = false;
    if (!_frameValid) return emitted != null;

    if (!_walker.WalkSlice(_rbsp, _rbspLength, header, sps, pps, _frameCells!, _mvField))
      Invalidate(_walker.LastFailure ?? "walk failed");

    return emitted != null;
  }

  private void BeginFrame(H265NalUnit nal, H265SpsExtended sps)
  {
    _activeSps = sps;
    _frameCells = new byte[sps.PicWidthInMinCb * sps.PicHeightInMinCb];
    _frameIsSyncSource = nal.IsSyncPoint;
    if (nal.IsSyncPoint || !_hasAnchor)
    {
      _anchorWall = nal.Timestamp;
      _anchorMedia = nal.MediaTimestamp;
      _hasAnchor = true;
      _frameTimestamp = nal.Timestamp;
    }
    else
    {
      _frameTimestamp = _anchorWall
        + (nal.MediaTimestamp - _anchorMedia) * MicrosecondsPerSecond / Rtp90kHzClock;
    }
    _frameIntraOnly = true;
    _frameValid = true;
    _frameInProgress = true;
    _mvField.BeginFrame(
      sps.PicWidthInLumaSamples >> 4, sps.PicHeightInLumaSamples >> 4);
    _walker.BeginFrame(sps);
  }

  private static string? Unsupported(H265SpsExtended sps, H265Pps pps)
  {
    if (sps.ChromaFormatIdc != ChromaFormat420)
      return $"chroma format {sps.ChromaFormatIdc} is not 4:2:0";
    if (sps.PcmEnabled) return "PCM coding is enabled";
    if (pps.TilesEnabledFlag) return "tiles are enabled";
    if (pps.EntropyCodingSyncEnabledFlag) return "wavefront parallel processing is enabled";
    if (pps.ExtensionPresentFlag) return "PPS extensions are present";
    return null;
  }

  private void Invalidate(string reason)
  {
    if (!_frameValid) return;
    _frameValid = false;
    LastFailure = reason;

    if (reason == _lastRejection) return;
    _lastRejection = reason;
    logger.LogWarning("Motion frame rejected: {Reason}", reason);
  }

  private MotionGridUnit? BuildUnit()
  {
    if (_frameCells == null || _activeSps == null) return null;
    if (!_frameValid)
    {
      DroppedFrames++;
      return null;
    }
    if (_frameIntraOnly)
    {
      _pendingSync |= _frameIsSyncSource;
      return null;
    }
    var unit = new MotionGridUnit
    {
      Data = _frameCells,
      Timestamp = _frameTimestamp,
      IsSyncPoint = _pendingSync,
      Width = (ushort)_activeSps.PicWidthInMinCb,
      Height = (ushort)_activeSps.PicHeightInMinCb
    };
    _pendingSync = false;
    return unit;
  }

  private void Reset()
  {
    _frameCells = null;
    _activeSps = null;
    _frameInProgress = false;
    _frameIntraOnly = false;
    _frameIsSyncSource = false;
    _frameValid = false;
  }
}
