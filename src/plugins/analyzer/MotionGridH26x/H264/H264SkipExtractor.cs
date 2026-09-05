using Microsoft.Extensions.Logging;
using Shared.Models.Formats;
using static Utils.BitstreamHelpers;

namespace Analyzer.MotionGridH26x;

internal sealed class H264SkipExtractor(ILogger logger)
{
  private const int NalRefIdcShift = 5;
  private const byte NalRefIdcMask = 0x3;
  private const byte NalUnitTypeMask = 0x1F;
  private const int NalHeaderBits = 8;
  private const ulong MicrosecondsPerSecond = 1_000_000UL;
  private const ulong Rtp90kHzClock = 90_000UL;

  private readonly Dictionary<uint, H264SpsExtended> _sps = [];
  private readonly Dictionary<uint, H264Pps> _pps = [];
  private readonly CavlcSliceWalker _cavlc = new();
  private readonly CabacSliceWalker _cabac = new();
  private readonly MotionVectorField _mvField = new();
  private IObserverHarness<ReconstructionPhase>? _observer;
  private byte[] _rbsp = [];

  private H264SpsExtended? _activeSps;
  private H264Pps? _activePps;
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

  internal void Observe(IObserverHarness<ReconstructionPhase> observer) =>
    _observer = observer;

  public bool TryFeed(H264NalUnit nal, out MotionGridUnit? emitted)
  {
    emitted = null;

    var data = nal.Data.Span;
    if (data.Length == 0) return false;
    var nalHeader = data[0];
    var nalRefIdc = (byte)((nalHeader >> NalRefIdcShift) & NalRefIdcMask);
    var nalUnitType = (byte)(nalHeader & NalUnitTypeMask);

    switch (nal.NalType)
    {
      case H264NalType.Sps:
        var sps = H264SpsExtended.Parse(data);
        _sps[sps.SeqParameterSetId] = sps;
        return false;

      case H264NalType.Pps:
        var pps = H264Pps.Parse(data);
        _pps[pps.PicParameterSetId] = pps;
        return false;

      case H264NalType.Slice:
        return ProcessSlice(data, nal, nalUnitType, nalRefIdc, out emitted);

      case H264NalType.Idr:
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

  private bool ProcessSlice(
    ReadOnlySpan<byte> rawNal, H264NalUnit nal,
    byte nalUnitType, byte nalRefIdc,
    out MotionGridUnit? emitted)
  {
    emitted = null;

    _observer?.Begin(ReconstructionPhase.Header);
    if (_rbsp.Length < rawNal.Length) _rbsp = new byte[rawNal.Length];
    var rbspLength = ExtractRbsp(rawNal, _rbsp);
    var rbsp = (ReadOnlySpan<byte>)_rbsp.AsSpan(0, rbspLength);
    var bitOffset = 0;

    Skip(ref bitOffset, NalHeaderBits);
    var peek = bitOffset;
    var firstMb = ReadExpGolomb(rbsp, ref peek);
    ReadExpGolomb(rbsp, ref peek);
    var ppsId = ReadExpGolomb(rbsp, ref peek);
    if (!_pps.TryGetValue(ppsId, out var pps))
    {
      _observer?.End(ReconstructionPhase.Header);
      return false;
    }
    if (!_sps.TryGetValue(pps.SeqParameterSetId, out var sps))
    {
      _observer?.End(ReconstructionPhase.Header);
      return false;
    }

    if (firstMb == 0 && _frameInProgress)
    {
      emitted = BuildUnit();
      Reset();
    }

    if (!_frameInProgress)
      BeginFrame(nal, sps, pps);

    var header = H264SliceHeader.Parse(rbsp, ref bitOffset, nalUnitType, nalRefIdc, sps, pps);
    _observer?.End(ReconstructionPhase.Header);

    MinSliceQp = Math.Min(MinSliceQp, header.SliceQpY);
    MaxSliceQp = Math.Max(MaxSliceQp, header.SliceQpY);

    if (!header.IsIntra)
      _frameIntraOnly = false;

    if (!_frameValid) return emitted != null;

    var cells = _frameCells!.AsSpan();
    if (pps.EntropyCodingModeFlag)
    {
      if (!_cabac.Walk(_rbsp, rbspLength, header, sps, pps, cells, _mvField, _observer))
        Invalidate(_cabac.LastFailure ?? "slice walker desynchronised");
    }
    else
    {
      if (!_cavlc.Walk(_rbsp, rbspLength, header, sps, pps, cells, _mvField, _observer))
        Invalidate(_cavlc.LastFailure ?? "slice walker desynchronised");
    }

    return emitted != null;
  }

  private void BeginFrame(H264NalUnit nal, H264SpsExtended sps, H264Pps pps)
  {
    _activeSps = sps;
    _activePps = pps;
    _frameCells = new byte[sps.PicSizeInMbs];
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
      _frameTimestamp = _anchorWall + (nal.MediaTimestamp - _anchorMedia) * MicrosecondsPerSecond / Rtp90kHzClock;
    }
    _frameIntraOnly = true;
    _frameValid = true;
    _frameInProgress = true;
    _mvField.BeginFrame(sps.PicWidthInMbs, sps.PicHeightInMapUnits);
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
      Width = (ushort)_activeSps.PicWidthInMbs,
      Height = (ushort)_activeSps.PicHeightInMapUnits
    };
    _pendingSync = false;
    return unit;
  }

  private void Reset()
  {
    _frameCells = null;
    _activeSps = null;
    _activePps = null;
    _frameInProgress = false;
    _frameIntraOnly = false;
    _frameIsSyncSource = false;
    _frameValid = false;
  }

}
