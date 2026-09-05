using H265;

namespace Analyzer.MotionGridH26x;

internal sealed class CabacSliceWalkerH265
{
  private const int CtxSaoMergeFlag = 0;
  private const int CtxSaoTypeIdx = 1;
  private const int CtxSplitCu = 2;
  private const int CtxCuTransquantBypass = 5;
  private const int CtxCuSkipFlag = 6;
  private const int CtxPredMode = 10;
  private const int CtxPartMode = 11;
  private const int CtxPrevIntraLumaPred = 15;
  private const int CtxIntraChromaPredMode = 16;
  private const int CtxRqtRootCbf = 17;
  private const int CtxSplitTransform = 126;
  private const int CtxCbfLuma = 129;
  private const int CtxCbfCbCr = 131;
  private const int CtxMergeFlag = 137;
  private const int CtxMergeIdx = 138;
  private const int CtxInterPredIdc = 139;
  private const int CtxRefIdx = 144;
  private const int CtxAbsMvdGreater0 = 146;
  private const int CtxAbsMvdGreater1 = 147;
  private const int CtxMvpFlag = 148;
  private const int CtxCuQpDeltaAbs = 150;

  private const int ChromaActivityShift = 2;
  private const int CuSkipShift = 7;
  private const int CuDepthMask = (1 << CuSkipShift) - 1;
  private const byte ModeDc = 1;
  private const int PartMode2Nx2N = 0;
  private const int PartMode2NxN = 1;
  private const int PartModeNx2N = 2;
  private const int PartModeNxN = 3;
  private const int InterDirBi = 3;
  private const int MvdEscapeAbs = 2;
  private const int ExpGolombCountCap = 24;
  private const int QpDeltaMagnitudeMax = 26;
  private const int TrailingBytesAllowance = 1;

  private static readonly int[] ChromaModeCandidates = [0, 26, 10, 1];

  private readonly CabacEngine _engine = new();
  private readonly ResidualReader _residuals = new();
  private readonly bool[] _prevFlags = new bool[4];
  private readonly int[] _mpmIdx = new int[4];

  private IObserverHarness<ReconstructionPhase>? _observer;

  private byte[] _cuMap = [];
  private byte[] _modes = [];
  private int _mapStride;
  private int _mapGuard;
  private int _cuStride;
  private int _cuGuard;
  private int _modeCells;
  private bool _modesFilled;

  private byte[] _cells = [];
  private int _picWidth;
  private int _picHeight;
  private int _log2Ctb;
  private int _log2MinCb;
  private int _log2MinTb;
  private int _log2MaxTb;
  private int _maxTtDepthInter;
  private int _maxTtDepthIntra;
  private bool _ampEnabled;
  private int _picWidthInCtbs;
  private int _picWidthInMinCb;
  private bool _cuQpDeltaEnabled;
  private bool _transquantBypassEnabled;
  private bool _transformSkipEnabled;
  private bool _signDataHiding;
  private bool _saoLuma;
  private bool _saoChroma;
  private int _numRefIdxL0;
  private int _numRefIdxL1;
  private bool _mvdL1Zero;
  private int _mergeCandMax;
  private int _sliceStartCtb;
  private bool _isB;
  private bool _cuQpDeltaCoded;
  private int _currentQp;
  private int _qpGroupLog2;
  private bool _tuTransformSkip;
  private bool _tuSignDataHiding;
  private int _chromaMode;

  private bool _cuIsIntra;
  private bool _cuIntraSplit;
  private bool _cuPartIs2Nx2N;
  private int _log2MinTbInCu;
  private int _cuActivity;
  private int _cuMv;
  private MotionVectorField _mv = null!;

  private bool _failed;
  private string _failReason = "";

  public string? LastFailure { get; private set; }

  public void Observe(IObserverHarness<ReconstructionPhase> observer)
  {
    _observer = observer;
    _residuals.Observe(observer);
  }

  public void BeginFrame(H265SpsExtended sps)
  {
    _mapStride = (sps.PicWidthInLumaSamples >> 2) + 1;
    _mapGuard = _mapStride + 1;
    _modeCells = _mapStride * ((sps.PicHeightInLumaSamples >> 2) + 1);
    if (_modes.Length < _modeCells) _modes = new byte[_modeCells];
    _modesFilled = false;

    _cuStride = (sps.PicWidthInLumaSamples >> 3) + 1;
    _cuGuard = _cuStride + 1;
    var cuCells = _cuStride * ((sps.PicHeightInLumaSamples >> 3) + 1);
    if (_cuMap.Length < cuCells) _cuMap = new byte[cuCells];
  }

  public bool WalkSlice(
    byte[] rbsp, int rbspLength, H265SliceHeader header,
    H265SpsExtended sps, H265Pps pps, byte[] cells, MotionVectorField mvField)
  {
    if (header.IsIntra) return true;

    _mv = mvField;
    _cells = cells;
    _picWidth = sps.PicWidthInLumaSamples;
    _picHeight = sps.PicHeightInLumaSamples;
    _log2Ctb = sps.Log2CtbSize;
    _log2MinCb = sps.Log2MinCbSize;
    _log2MinTb = sps.Log2MinTbSize;
    _log2MaxTb = sps.Log2MaxTbSize;
    _maxTtDepthInter = sps.MaxTransformDepthInter;
    _maxTtDepthIntra = sps.MaxTransformDepthIntra;
    _ampEnabled = sps.AmpEnabled;
    _picWidthInCtbs = sps.PicWidthInCtbs;
    _picWidthInMinCb = sps.PicWidthInMinCb;
    _cuQpDeltaEnabled = pps.CuQpDeltaEnabledFlag;
    _transquantBypassEnabled = pps.TransquantBypassEnabledFlag;
    _transformSkipEnabled = pps.TransformSkipEnabledFlag;
    _signDataHiding = pps.SignDataHidingEnabledFlag;
    _saoLuma = header.SaoLuma;
    _saoChroma = header.SaoChroma;
    _numRefIdxL0 = header.NumRefIdxL0;
    _numRefIdxL1 = header.NumRefIdxL1;
    _mvdL1Zero = header.MvdL1Zero;
    _mergeCandMax = (int)header.MaxNumMergeCand - 1;
    _sliceStartCtb = (int)header.SliceSegmentAddress;
    _isB = header.SliceType == H265SliceType.B;
    _tuTransformSkip = _transformSkipEnabled;
    _tuSignDataHiding = _signDataHiding;
    _qpGroupLog2 = sps.Log2CtbSize - (int)pps.DiffCuQpDeltaDepth;
    _currentQp = header.SliceQpY;
    _failed = false;
    _failReason = "";
    LastFailure = null;

    _observer?.Begin(ReconstructionPhase.Header);
    _engine.Initialize(
      rbsp, rbspLength, header.BitOffsetAfterHeader, header.SliceQpY, ResolveInitType(header));
    _observer?.End(ReconstructionPhase.Header);

    var picWidthInCtbs = _picWidthInCtbs;
    var ctbSize = 1 << _log2Ctb;
    var totalCtbs = sps.PicSizeInCtbsY;
    var ctbAddr = _sliceStartCtb;
    var ctbX = ctbAddr % picWidthInCtbs;
    var ctbY = ctbAddr / picWidthInCtbs;
    var walkSao = sps.SaoEnabled && (_saoLuma || _saoChroma);
    var ended = false;

    while (ctbAddr < totalCtbs)
    {
      var x = ctbX << _log2Ctb;
      var y = ctbY << _log2Ctb;

      if (walkSao)
      {
        _observer?.Begin(ReconstructionPhase.Sao);
        WalkSao(ctbAddr, ctbX, picWidthInCtbs);
        _observer?.End(ReconstructionPhase.Sao);
      }

      if (x + ctbSize <= _picWidth && y + ctbSize <= _picHeight)
        WalkQuadtreeInterior(x, y, _log2Ctb, 0);
      else
        WalkQuadtree(x, y, _log2Ctb, 0);
      if (_failed) return Fail($"desynchronised in CTB ({x},{y}): {_failReason}");

      ctbAddr++;
      if (++ctbX == picWidthInCtbs)
      {
        ctbX = 0;
        ctbY++;
      }
      if (_engine.DecodeTerminate() == 1)
      {
        ended = true;
        break;
      }
    }

    if (!ended)
      return Fail($"slice ran past CTB {totalCtbs}, " +
        $"{_engine.BytesRead} of {rbspLength} bytes consumed");

    for (var i = _engine.BytesRead + TrailingBytesAllowance; i < rbspLength; i++)
      if (rbsp[i] != 0)
        return Fail($"slice ended at CTB {ctbAddr} of {totalCtbs} " +
          $"with data left, {_engine.BytesRead} of {rbspLength} bytes consumed");

    return true;
  }

  private static H264.CabacInitType ResolveInitType(H265SliceHeader header) =>
    header.SliceType == H265SliceType.P
      ? header.CabacInitFlag ? H264.CabacInitType.Inter1 : H264.CabacInitType.Inter0
      : header.CabacInitFlag ? H264.CabacInitType.Inter0 : H264.CabacInitType.Inter1;

  private bool Fail(string reason)
  {
    LastFailure = reason;
    return false;
  }

  private void FailCu(string reason)
  {
    if (_failed) return;
    _failed = true;
    _failReason = reason;
  }

  private void WalkSao(int ctbAddr, int ctbX, int picWidthInCtbs)
  {
    var leftAvail = ctbX > 0 && ctbAddr - 1 >= _sliceStartCtb;
    var upAvail = ctbAddr - picWidthInCtbs >= _sliceStartCtb;

    if (leftAvail && _engine.DecodeDecision(CtxSaoMergeFlag) == 1) return;
    if (upAvail && _engine.DecodeDecision(CtxSaoMergeFlag) == 1) return;

    var chromaTypeIdx = 0;
    for (var component = 0; component < 3; component++)
    {
      if (!(component == 0 ? _saoLuma : _saoChroma)) continue;

      int typeIdx;
      if (component == 2)
      {
        typeIdx = chromaTypeIdx;
      }
      else
      {
        typeIdx = ReadSaoTypeIdx();
        if (component == 1) chromaTypeIdx = typeIdx;
      }

      if (typeIdx == 0) continue;

      var offset0 = _engine.DecodeBypassUnary(7);
      var offset1 = _engine.DecodeBypassUnary(7);
      var offset2 = _engine.DecodeBypassUnary(7);
      var offset3 = _engine.DecodeBypassUnary(7);

      if (typeIdx == 1)
      {
        if (offset0 != 0) _engine.DecodeBypass();
        if (offset1 != 0) _engine.DecodeBypass();
        if (offset2 != 0) _engine.DecodeBypass();
        if (offset3 != 0) _engine.DecodeBypass();
        _engine.DecodeBypassBits(5);
      }
      else if (component < 2)
      {
        _engine.DecodeBypassBits(2);
      }
    }
  }

  private int ReadSaoTypeIdx()
  {
    if (_engine.DecodeDecision(CtxSaoTypeIdx) == 0) return 0;
    return _engine.DecodeBypass() == 1 ? 2 : 1;
  }

  private void WalkQuadtree(int x, int y, int log2CbSize, int depth)
  {
    if (_failed) return;
    if (x >= _picWidth || y >= _picHeight) return;

    var split = log2CbSize > _log2MinCb;
    if (split
        && x + (1 << log2CbSize) <= _picWidth
        && y + (1 << log2CbSize) <= _picHeight)
      split = _engine.DecodeDecision(CtxSplitCu + SplitContext(x, y, depth)) == 1;

    if (_cuQpDeltaEnabled
        && (log2CbSize == _qpGroupLog2 || (!split && log2CbSize > _qpGroupLog2)))
      _cuQpDeltaCoded = false;

    if (split)
    {
      var half = 1 << (log2CbSize - 1);
      WalkQuadtree(x, y, log2CbSize - 1, depth + 1);
      WalkQuadtree(x + half, y, log2CbSize - 1, depth + 1);
      WalkQuadtree(x, y + half, log2CbSize - 1, depth + 1);
      WalkQuadtree(x + half, y + half, log2CbSize - 1, depth + 1);
      return;
    }

    WalkCu(x, y, log2CbSize, depth);
  }

  private void WalkQuadtreeInterior(int x, int y, int log2CbSize, int depth)
  {
    if (_failed) return;

    var split = log2CbSize > _log2MinCb
      && _engine.DecodeDecision(CtxSplitCu + SplitContext(x, y, depth)) == 1;

    if (_cuQpDeltaEnabled
        && (log2CbSize == _qpGroupLog2 || (!split && log2CbSize > _qpGroupLog2)))
      _cuQpDeltaCoded = false;

    if (split)
    {
      var half = 1 << (log2CbSize - 1);
      WalkQuadtreeInterior(x, y, log2CbSize - 1, depth + 1);
      WalkQuadtreeInterior(x + half, y, log2CbSize - 1, depth + 1);
      WalkQuadtreeInterior(x, y + half, log2CbSize - 1, depth + 1);
      WalkQuadtreeInterior(x + half, y + half, log2CbSize - 1, depth + 1);
      return;
    }

    WalkCu(x, y, log2CbSize, depth);
  }

  private int SplitContext(int x, int y, int depth)
  {
    if (_sliceStartCtb != 0) return SplitContextSliced(x, y, depth);

    var left = _cuMap[MapIndexCu(x - 1, y)] & CuDepthMask;
    var above = _cuMap[MapIndexCu(x, y - 1)] & CuDepthMask;
    return (int)((uint)(depth - left) >> 31) + (int)((uint)(depth - above) >> 31);
  }

  private int SplitContextSliced(int x, int y, int depth)
  {
    var inc = 0;
    if (Available(x - 1, y) && (_cuMap[MapIndexCu(x - 1, y)] & CuDepthMask) > depth) inc++;
    if (Available(x, y - 1) && (_cuMap[MapIndexCu(x, y - 1)] & CuDepthMask) > depth) inc++;
    return inc;
  }

  private bool Available(int x, int y)
  {
    if (x < 0 || y < 0) return false;
    if (_sliceStartCtb == 0) return true;
    var ctbAddr = (y >> _log2Ctb) * _picWidthInCtbs + (x >> _log2Ctb);
    return ctbAddr >= _sliceStartCtb;
  }

  private int MapIndex(int x, int y) => (y >> 2) * _mapStride + (x >> 2) + _mapGuard;

  private int MapIndexCu(int x, int y) => (y >> 3) * _cuStride + (x >> 3) + _cuGuard;

  private void Store(byte[] map, int x, int y, int size, byte value)
  {
    var entries = size >> 2;
    var first = MapIndex(x, y);

    if (entries == 1)
    {
      if (first < map.Length) map[first] = value;
      return;
    }

    var rows = entries;
    for (var row = first; rows > 0 && row < map.Length; row += _mapStride, rows--)
      map.AsSpan(row, Math.Min(entries, map.Length - row)).Fill(value);
  }

  private void StoreCuEdges(int x, int y, int size, byte value)
  {
    var entries = size >> 3;

    _cuMap.AsSpan(MapIndexCu(x, y + size - 8), entries).Fill(value);

    var right = MapIndexCu(x + size - 8, y);
    for (var rows = entries; rows > 0; rows--, right += _cuStride)
      _cuMap[right] = value;
  }

  private void WalkCu(int x, int y, int log2CbSize, int depth)
  {
    if (_transquantBypassEnabled)
    {
      var bypass = _engine.DecodeDecision(CtxCuTransquantBypass) == 1;
      _tuTransformSkip = _transformSkipEnabled && !bypass;
      _tuSignDataHiding = _signDataHiding && !bypass;
    }

    int skipCtx;
    if (_sliceStartCtb == 0)
    {
      skipCtx = (_cuMap[MapIndexCu(x - 1, y)] >> CuSkipShift)
        + (_cuMap[MapIndexCu(x, y - 1)] >> CuSkipShift);
    }
    else
    {
      skipCtx = 0;
      if (Available(x - 1, y) && _cuMap[MapIndexCu(x - 1, y)] >> CuSkipShift != 0) skipCtx++;
      if (Available(x, y - 1) && _cuMap[MapIndexCu(x, y - 1)] >> CuSkipShift != 0) skipCtx++;
    }

    var skipBin = _engine.DecodeDecision(CtxCuSkipFlag + skipCtx);
    var skipped = skipBin == 1;

    _observer?.Block(!skipped);
    StoreCuEdges(x, y, 1 << log2CbSize, (byte)(depth | (skipBin << CuSkipShift)));

    _observer?.Begin(ReconstructionPhase.Header);

    if (skipped)
    {
      WalkMergeIdx();
      _observer?.End(ReconstructionPhase.Header);
      var index = _mv.Index(x >> 4, y >> 4);
      var mv = _mv.Corroborated(index);
      if (mv != 0)
      {
        var value = (byte)Math.Min(byte.MaxValue,
          MotionScore.MvTerm(mv, _mv, index, SizePenalty(log2CbSize)));
        if (value != 0)
          StampCells(x, y, log2CbSize, value);
      }
      _mv.StoreSquare(index, Math.Max(1, (1 << log2CbSize) >> 4), mv);
      return;
    }

    _cuActivity = 0;
    _cuMv = 0;
    var intra = _engine.DecodeDecision(CtxPredMode) == 1;
    if (intra)
      WalkIntraCu(x, y, log2CbSize);
    else
      WalkInterCu(x, y, log2CbSize, depth);

    if (_failed) return;
    StampCells(x, y, log2CbSize, ActivityValue(x, y, log2CbSize));
  }

  private byte ActivityValue(int x, int y, int log2CbSize)
  {
    var cellShift = 2 * (log2CbSize - _log2MinCb);
    var visualActivity = MotionScore.VisualActivity(_cuActivity, _currentQp);
    return (byte)Math.Min(byte.MaxValue,
      (visualActivity >> cellShift)
      + MotionScore.MvTerm(_cuMv, _mv, _mv.Index(x >> 4, y >> 4), SizePenalty(log2CbSize)));
  }

  private static int SizePenalty(int log2CbSize) =>
    Math.Max(0, log2CbSize - 4);

  private void WalkIntraCu(int x, int y, int log2CbSize)
  {
    _mv.StoreSquare(_mv.Index(x >> 4, y >> 4), Math.Max(1, (1 << log2CbSize) >> 4), 0);

    if (!_modesFilled)
    {
      Array.Fill(_modes, ModeDc, 0, _modeCells);
      _modesFilled = true;
    }

    var partNxN = false;
    if (log2CbSize == _log2MinCb && log2CbSize > 2)
      partNxN = _engine.DecodeDecision(CtxPartMode) == 0;

    var parts = partNxN ? 4 : 1;
    var partSize = 1 << (log2CbSize - (partNxN ? 1 : 0));

    Span<bool> prevFlags = _prevFlags;
    Span<int> mpmIdx = _mpmIdx;

    for (var i = 0; i < parts; i++)
      prevFlags[i] = _engine.DecodeDecision(CtxPrevIntraLumaPred) == 1;

    for (var i = 0; i < parts; i++)
      mpmIdx[i] = prevFlags[i]
        ? _engine.DecodeBypassUnary(2)
        : (int)_engine.DecodeBypassBits(5);

    int firstLumaMode = ModeDc;
    for (var i = 0; i < parts; i++)
    {
      var px = x + (i & 1) * partSize;
      var py = y + (i >> 1) * partSize;
      var mode = DeriveLumaMode(px, py, prevFlags[i], mpmIdx[i]);
      if (i == 0) firstLumaMode = mode;
      Store(_modes, px, py, partSize, (byte)mode);
    }

    _chromaMode = ReadChromaMode(firstLumaMode);

    _cuIsIntra = true;
    _cuIntraSplit = partNxN;
    _cuPartIs2Nx2N = !partNxN;
    _log2MinTbInCu = Log2MinTbInCu(log2CbSize);

    _observer?.End(ReconstructionPhase.Header);
    WalkTransformTree(x, y, x, y, log2CbSize, 0, 0, true, true);
  }

  private void WalkInterCu(int x, int y, int log2CbSize, int depth)
  {
    var partMode = PartMode2Nx2N;
    var maxBits = 2;
    if (log2CbSize == _log2MinCb && log2CbSize > 3) maxBits = 3;

    for (var i = 0; i < maxBits; i++)
    {
      if (_engine.DecodeDecision(CtxPartMode + i) == 1) break;
      partMode++;
    }

    var puCount = partMode == PartMode2Nx2N ? 1 : partMode == PartModeNxN ? 4 : 2;

    if (_ampEnabled && log2CbSize > _log2MinCb
        && partMode is PartMode2NxN or PartModeNx2N
        && _engine.DecodeDecision(CtxPartMode + 3) == 0)
      _engine.DecodeBypass();

    var firstPuMerged = false;
    var cuMvSet = false;
    for (var pu = 0; pu < puCount; pu++)
    {
      var merged = _engine.DecodeDecision(CtxMergeFlag) == 1;
      if (pu == 0) firstPuMerged = merged;

      if (merged)
      {
        WalkMergeIdx();
        if (!cuMvSet)
        {
          _cuMv = _mv.Corroborated(_mv.Index(x >> 4, y >> 4));
          cuMvSet = true;
        }
        continue;
      }

      var interDir = 1;
      if (_isB)
      {
        var biPossible = partMode == PartMode2Nx2N || log2CbSize != 3;
        var bi = biPossible && _engine.DecodeDecision(CtxInterPredIdc + depth) == 1;
        interDir = bi ? InterDirBi : 1 + _engine.DecodeDecision(CtxInterPredIdc + 4);
      }

      for (var list = 0; list < 2; list++)
      {
        if ((interDir & (1 << list)) == 0) continue;

        var numRefIdx = list == 0 ? _numRefIdxL0 : _numRefIdxL1;
        if (numRefIdx > 1)
          WalkRefIdx(numRefIdx);

        var mvd = 0;
        if (!(_mvdL1Zero && list == 1 && interDir == InterDirBi))
          mvd = WalkMvd();

        var predictorAbove = _engine.DecodeDecision(CtxMvpFlag) == 1;
        if (!cuMvSet)
        {
          var index = _mv.Index(x >> 4, y >> 4);
          var predictor = predictorAbove ? _mv.Above(index) : _mv.Left(index);
          _cuMv = MotionScore.AddMv(predictor, mvd);
          cuMvSet = true;
        }
      }
    }

    _mv.StoreSquare(_mv.Index(x >> 4, y >> 4), Math.Max(1, (1 << log2CbSize) >> 4), _cuMv);

    var rootCbf = partMode == PartMode2Nx2N && firstPuMerged
      || _engine.DecodeDecision(CtxRqtRootCbf) == 1;
    if (!rootCbf)
    {
      _observer?.End(ReconstructionPhase.Header);
      return;
    }

    _cuIsIntra = false;
    _cuIntraSplit = false;
    _cuPartIs2Nx2N = partMode == PartMode2Nx2N;
    _log2MinTbInCu = Log2MinTbInCu(log2CbSize);

    _observer?.End(ReconstructionPhase.Header);
    WalkTransformTree(x, y, x, y, log2CbSize, 0, 0, true, true);
  }

  private int Log2MinTbInCu(int log2CbSize)
  {
    var maxDepth = 1 + (_cuIsIntra ? _maxTtDepthIntra : _maxTtDepthInter);
    var intraSplit = _cuIsIntra && !_cuPartIs2Nx2N ? 1 : 0;
    var interSplit =
      !_cuIsIntra && _maxTtDepthInter == 0 && !_cuPartIs2Nx2N ? 1 : 0;

    if (log2CbSize < _log2MinTb + maxDepth - 1 + interSplit + intraSplit)
      return _log2MinTb;

    return Math.Min(
      log2CbSize - (maxDepth - 1 + interSplit + intraSplit), _log2MaxTb);
  }

  private void WalkMergeIdx()
  {
    var cMax = _mergeCandMax;
    if (cMax <= 0) return;
    if (_engine.DecodeDecision(CtxMergeIdx) == 0) return;
    _engine.DecodeBypassUnary(cMax - 1);
  }

  private void WalkRefIdx(int numRefIdx)
  {
    if (_engine.DecodeDecision(CtxRefIdx) == 0) return;
    for (var i = 0; i < numRefIdx - 2; i++)
    {
      var bin = i == 0 ? _engine.DecodeDecision(CtxRefIdx + 1) : _engine.DecodeBypass();
      if (bin == 0) return;
    }
  }

  private int WalkMvd()
  {
    var horGreater0 = _engine.DecodeDecision(CtxAbsMvdGreater0) == 1;
    var verGreater0 = _engine.DecodeDecision(CtxAbsMvdGreater0) == 1;
    var horGreater1 = horGreater0 && _engine.DecodeDecision(CtxAbsMvdGreater1) == 1;
    var verGreater1 = verGreater0 && _engine.DecodeDecision(CtxAbsMvdGreater1) == 1;

    var mvx = 0;
    if (horGreater0)
    {
      mvx = horGreater1 ? MvdEscapeAbs + DecodeEpExGolomb(1) : 1;
      if (_engine.DecodeBypass() == 1) mvx = -mvx;
    }
    var mvy = 0;
    if (verGreater0)
    {
      mvy = verGreater1 ? MvdEscapeAbs + DecodeEpExGolomb(1) : 1;
      if (_engine.DecodeBypass() == 1) mvy = -mvy;
    }
    return MotionScore.PackMv(mvx, mvy);
  }

  private int DecodeEpExGolomb(int order)
  {
    var limit = ExpGolombCountCap - order + 1;
    var prefix = _engine.DecodeBypassUnary(limit);
    if (prefix == limit)
    {
      FailCu("runaway exp-golomb prefix");
      return 0;
    }

    var width = order + prefix;
    var symbol = ((1 << prefix) - 1) << order;
    if (width > 0)
      symbol += (int)_engine.DecodeBypassBits(width);
    return symbol;
  }

  private int DeriveLumaMode(int x, int y, bool prevFlag, int idx)
  {
    var candA = ModeAt(x - 1, y);
    var candB = y > 0 && (y & ((1 << _log2Ctb) - 1)) != 0 ? ModeAt(x, y - 1) : ModeDc;

    int first, second, third;
    if (candA == candB)
    {
      if (candA < 2)
      {
        first = 0;
        second = 1;
        third = 26;
      }
      else
      {
        first = candA;
        second = 2 + ((candA + 29) & 31);
        third = 2 + ((candA - 1) & 31);
      }
    }
    else
    {
      first = candA;
      second = candB;
      third = candA != 0 && candB != 0 ? 0 : candA != 1 && candB != 1 ? 1 : 26;
    }

    if (prevFlag)
      return idx == 0 ? first : idx == 1 ? second : third;

    var low = Math.Min(first, second);
    var high = Math.Max(first, second);
    first = Math.Min(low, third);
    var middle = Math.Max(low, third);
    second = Math.Min(high, middle);
    third = Math.Max(high, middle);

    var mode = idx;
    mode += mode >= first ? 1 : 0;
    mode += mode >= second ? 1 : 0;
    mode += mode >= third ? 1 : 0;
    return mode;
  }

  private int ModeAt(int x, int y) =>
    _sliceStartCtb == 0 || Available(x, y) ? _modes[MapIndex(x, y)] : ModeDc;

  private int ReadChromaMode(int lumaMode)
  {
    if (_engine.DecodeDecision(CtxIntraChromaPredMode) == 0)
      return lumaMode;

    var mode = ChromaModeCandidates[(int)_engine.DecodeBypassBits(2)];
    return mode == lumaMode ? 34 : mode;
  }

  private void WalkTransformTree(
    int x, int y, int xBase, int yBase, int log2TrSize, int trDepth, int blkIdx,
    bool parentCbfCb, bool parentCbfCr)
  {
    if (_failed) return;

    _observer?.Begin(ReconstructionPhase.Header);

    bool subdiv;
    if (_cuIsIntra && _cuIntraSplit && trDepth == 0)
      subdiv = true;
    else if (!_cuIsIntra && _maxTtDepthInter == 0 && !_cuPartIs2Nx2N && trDepth == 0)
      subdiv = log2TrSize > _log2MinTbInCu;
    else if (log2TrSize > _log2MaxTb)
      subdiv = true;
    else if (log2TrSize == _log2MinTb || log2TrSize == _log2MinTbInCu)
      subdiv = false;
    else
      subdiv = _engine.DecodeDecision(CtxSplitTransform + 5 - log2TrSize) == 1;

    var cbfCb = parentCbfCb;
    var cbfCr = parentCbfCr;
    if (log2TrSize > 2)
    {
      if (parentCbfCb)
        cbfCb = _engine.DecodeDecision(CtxCbfCbCr + trDepth) == 1;
      if (parentCbfCr)
        cbfCr = _engine.DecodeDecision(CtxCbfCbCr + trDepth) == 1;
    }

    if (subdiv)
    {
      _observer?.End(ReconstructionPhase.Header);

      var half = 1 << (log2TrSize - 1);
      WalkTransformTree(x, y, x, y, log2TrSize - 1, trDepth + 1, 0, cbfCb, cbfCr);
      WalkTransformTree(x + half, y, x, y, log2TrSize - 1, trDepth + 1, 1, cbfCb, cbfCr);
      WalkTransformTree(x, y + half, x, y, log2TrSize - 1, trDepth + 1, 2, cbfCb, cbfCr);
      WalkTransformTree(x + half, y + half, x, y, log2TrSize - 1, trDepth + 1, 3, cbfCb, cbfCr);
      return;
    }

    var cbfLuma = true;
    if (_cuIsIntra || trDepth != 0 || cbfCb || cbfCr)
      cbfLuma = _engine.DecodeDecision(CtxCbfLuma + (trDepth == 0 ? 1 : 0)) == 1;

    _observer?.End(ReconstructionPhase.Header);
    WalkTransformUnit(x, y, xBase, yBase, log2TrSize, blkIdx, cbfLuma, cbfCb, cbfCr);
  }

  private void WalkTransformUnit(
    int x, int y, int xBase, int yBase, int log2TrSize, int blkIdx,
    bool cbfLuma, bool cbfCb, bool cbfCr)
  {
    if (_cuQpDeltaEnabled && !_cuQpDeltaCoded && (cbfLuma || cbfCb || cbfCr))
    {
      _observer?.Begin(ReconstructionPhase.Header);
      ReadQpDelta();
      _cuQpDeltaCoded = true;
      _observer?.End(ReconstructionPhase.Header);
      if (_failed) return;
    }

    if (cbfLuma && !ReadResidual(x, y, log2TrSize, 0))
      return;

    var chromaHere = log2TrSize > 2 || blkIdx == 3;
    if (!chromaHere) return;

    var chromaLog2 = log2TrSize > 2 ? log2TrSize - 1 : log2TrSize;
    var cx = (log2TrSize > 2 ? x : xBase) >> 1;
    var cy = (log2TrSize > 2 ? y : yBase) >> 1;

    if (cbfCb && !ReadResidual(cx, cy, chromaLog2, 1))
      return;
    if (cbfCr)
      ReadResidual(cx, cy, chromaLog2, 2);
  }

  private bool ReadResidual(int x, int y, int log2TrSize, int cIdx)
  {
    var scanIdx = _cuIsIntra
      ? ScanFor(cIdx == 0 ? ModeAt(x, y) : _chromaMode, log2TrSize, cIdx)
      : ScanIdx.Diagonal;

    var activity = 0;
    var read = _residuals.ReadActivity(
      _engine, log2TrSize, cIdx, scanIdx,
      _tuTransformSkip, _tuSignDataHiding, ref activity);
    _cuActivity += cIdx == 0 ? activity : activity >> ChromaActivityShift;

    if (!read)
      FailCu($"implausible residual at ({x},{y}), " +
        $"{1 << log2TrSize}x{1 << log2TrSize} cIdx {cIdx}");
    return read;
  }

  private static ScanIdx ScanFor(int mode, int log2TrSize, int cIdx)
  {
    if (log2TrSize != 2 && !(log2TrSize == 3 && cIdx == 0))
      return ScanIdx.Diagonal;
    if (mode is >= 6 and <= 14) return ScanIdx.Vertical;
    if (mode is >= 22 and <= 30) return ScanIdx.Horizontal;
    return ScanIdx.Diagonal;
  }

  private void ReadQpDelta()
  {
    var magnitude = 0;
    while (magnitude < 5
           && _engine.DecodeDecision(CtxCuQpDeltaAbs + (magnitude > 0 ? 1 : 0)) == 1)
      magnitude++;

    if (magnitude == 5)
      magnitude += DecodeEpExGolomb(0);

    if (magnitude == 0) return;

    if (magnitude > QpDeltaMagnitudeMax)
    {
      FailCu($"cu_qp_delta_abs {magnitude} exceeds the legal range");
      return;
    }

    var negative = _engine.DecodeBypass() == 1;
    _currentQp = Math.Clamp(_currentQp + (negative ? -magnitude : magnitude), 0, 51);
  }

  private void StampCells(int x, int y, int log2CbSize, byte value)
  {
    var log2MinCb = _log2MinCb;
    var size = 1 << log2CbSize;
    var picWidthCells = _picWidthInMinCb;

    var startCellX = x >> log2MinCb;
    var width = ((x + size) >> log2MinCb) - startCellX;
    var endCellY = (y + size) >> log2MinCb;

    for (var cy = y >> log2MinCb; cy < endCellY; cy++)
      _cells.AsSpan(cy * picWidthCells + startCellX, width).Fill(value);
  }
}
