using H264;

namespace Analyzer.MotionGridH26x;

internal sealed class CabacSliceWalker
{
  private const byte MbCoded = 255;
  private const int SubCellShift = 2;
  private const int ChromaActivityShift = 2;

  private const int CtxSkipP = 11;
  private const int CtxSkipB = 24;
  private const int CtxMbTypeIntraFlag = 14;
  private const int CtxMbTypeSplit = 15;
  private const int CtxMbTypeNarrow = 16;
  private const int CtxMbTypeWide = 17;
  private const int CtxMbTypePIntra = 17;
  private const int CtxMbTypeB = 27;
  private const int CtxMbTypeBSplit = 30;
  private const int CtxMbTypeBWide = 31;
  private const int CtxMbTypeBBits = 32;
  private const int CtxMbTypeBIntra = 32;
  private const int CtxSubMbType = 21;
  private const int CtxBSubMbType = 36;
  private const int CtxMvdX = 40;
  private const int CtxMvdY = 47;
  private const int CtxRefIdxFirst = 54;
  private const int CtxRefIdxSecond = 58;
  private const int CtxRefIdxRest = 59;
  private const int CtxPrevIntraPredMode = 68;
  private const int CtxRemIntraPredMode = 69;
  private const int CtxTransform8x8 = 399;

  private const int RemIntraPredModeBits = 3;
  private const int MvdPrefixBins = 8;
  private const int MvdExpGolombOrder = 3;
  private const int ExpGolombOrderCap = 20;
  private const int RefIdxCap = 32;
  private const int Pcm420Bytes = 384;
  private const byte ChromaFormat420 = 1;
  private const int Outside = BlockOrder.Outside;

  private static readonly byte[] MvdBinCtx = [0, 1, 2, 3, 3, 3, 3, 3];

  private readonly CabacEngine _engine = new();
  private readonly ResidualReader _residuals = new();

  private Neighbour[] _above = [];
  private Neighbour _outside;
  private sbyte[] _aboveMasks = [];
  private byte[] _aboveNotSkipped = [];
  private int _leftNotSkipped;
  private int _leftMask;
  private int _aboveMask;
  private readonly byte[][] _aboveRef = [[], []];
  private readonly ushort[][] _aboveMvdX = [[], []];
  private readonly ushort[][] _aboveMvdY = [[], []];
  private readonly byte[][] _leftRef = [new byte[4], new byte[4]];
  private readonly ushort[][] _leftMvdX = [new ushort[4], new ushort[4]];
  private readonly ushort[][] _leftMvdY = [new ushort[4], new ushort[4]];

  private int _lastQpDelta;
  private int _mbActivity;
  private int _mbChromaActivity;
  private int _mbMvd;
  private bool _mbMvdSet;
  private int _currentQp;
  private MotionVectorField _field = null!;
  private IObserverHarness<ReconstructionPhase>? _observer;
  private Neighbour _skipState;
  private bool _isB;
  private int _refIdxL0Active;
  private int _refIdxL1Active;
  private bool _transform8x8Mode;
  private bool _direct8x8Inference;

  public string? LastFailure { get; private set; }

  public bool Walk(
    byte[] rbsp, int rbspLength, H264SliceHeader header, H264SpsExtended sps, H264Pps pps,
    Span<byte> cells, MotionVectorField field, IObserverHarness<ReconstructionPhase>? observer)
  {
    LastFailure = null;

    if (header.IsIntra)
    {
      for (var i = (int)header.FirstMbInSlice; i < cells.Length; i++)
        cells[i] = MbCoded;
      return true;
    }

    if (sps.ChromaArrayType != ChromaFormat420)
      return Fail($"chroma array type {sps.ChromaArrayType} is not supported");

    var widthMbs = sps.PicWidthInMbs;
    PrepareRows(widthMbs);
    _observer = observer;
    if (observer != null) _residuals.Observe(observer);

    _engine.Initialize(rbsp, rbspLength, header.BitOffsetAfterHeader, header.SliceQpY,
      (CabacInitType)(header.CabacInitIdc + 1));

    var isB = header.SliceType == H264SliceType.B;
    _isB = isB;
    _skipState = new Neighbour { Available = true, Skipped = true, Direct = isB };
    _refIdxL0Active = (int)header.NumRefIdxL0Active;
    _refIdxL1Active = (int)header.NumRefIdxL1Active;
    _transform8x8Mode = pps.Transform8x8ModeFlag;
    _direct8x8Inference = sps.Direct8x8InferenceFlag;
    _field = field;
    _currentQp = header.SliceQpY;
    var skipCtx = isB ? CtxSkipB : CtxSkipP;
    var totalMbs = sps.PicSizeInMbs;
    var mbAddr = (int)header.FirstMbInSlice;
    var mbX = mbAddr % widthMbs;
    var fieldIndex = field.Index(mbX, mbAddr / widthMbs);
    var ended = false;

    while (mbAddr < totalMbs)
    {
      if (mbX == 0) ResetLeft();

      var skipInc = _leftNotSkipped + _aboveNotSkipped[mbX];
      var skipped = _engine.DecodeDecision(skipCtx + skipInc) == 1;
      observer?.Block(!skipped);

      if (skipped)
      {
        CommitSkipped(mbX);
        var mv = field.SkipPredictor(fieldIndex);
        if (mv != 0)
        {
          var term = MotionScore.MvTerm(mv, field, fieldIndex, 0);
          if (term != 0)
            cells[mbAddr] = (byte)Math.Min(byte.MaxValue, term);
        }
        field.Store(fieldIndex, mv);
      }
      else
      {
        observer?.Begin(ReconstructionPhase.Header);
        if (!WalkCodedMb(mbX, fieldIndex, out var value))
          return false;
        cells[mbAddr] = value;
      }

      mbAddr++;
      fieldIndex++;
      if (++mbX == widthMbs)
      {
        mbX = 0;
        fieldIndex++;
      }
      if (_engine.DecodeTerminate() == 1)
      {
        ended = true;
        break;
      }
    }

    if (!ended)
      return Fail($"slice ran past macroblock {totalMbs}, " +
        $"{_engine.BytesRead} of {_engine.BytesTotal} bytes consumed");
    return true;
  }

  private bool WalkCodedMb(int mbX, int fieldIndex, out byte value)
  {
    var isB = _isB;
    var observer = _observer;
    value = 0;
    _mbActivity = 0;
    _mbChromaActivity = 0;
    _mbMvd = 0;
    _mbMvdSet = false;
    _aboveMask = _aboveMasks[mbX];

    var state = new Neighbour { Available = true };
    var cbpLuma = 0;
    var cbpChroma = 0;
    var isIntra = false;
    var isIntra16 = false;
    var transform8x8 = false;
    var direct16 = false;
    var interType = 0;

    ref var left = ref Left(mbX);
    ref var above = ref _above[mbX];

    if (isB)
    {
      var inc = (left.Available && !left.Direct ? 1 : 0)
              + (above.Available && !above.Direct ? 1 : 0);
      interType = DecodeBMbType(inc);
      isIntra = interType == BMbTypes.IntraOffset;
    }
    else
    {
      isIntra = _engine.DecodeDecision(CtxMbTypeIntraFlag) == 1;
    }

    if (isIntra)
    {
      var suffixBase = isB ? CtxMbTypeBIntra : CtxMbTypePIntra;
      isIntra16 = _engine.DecodeDecision(suffixBase) == 1;
      if (isIntra16)
      {
        if (_engine.DecodeTerminate() == 1)
        {
          observer?.End(ReconstructionPhase.Header);
          if (!SkipPcm(mbX)) return false;
          value = MbCoded;
          return true;
        }

        cbpLuma = _engine.DecodeDecision(suffixBase + 1) == 1 ? 15 : 0;
        if (_engine.DecodeDecision(suffixBase + 2) == 1)
          cbpChroma = _engine.DecodeDecision(suffixBase + 2) == 1 ? 2 : 1;
        _engine.DecodeDecision(suffixBase + 3);
        _engine.DecodeDecision(suffixBase + 3);

        state.ChromaPredModeNonZero =
          MacroblockReader.ReadChromaPredMode(_engine, left, above) != 0;
      }
      else
      {
        if (_transform8x8Mode)
        {
          var inc = (left.Available && left.Transform8x8 ? 1 : 0)
                  + (above.Available && above.Transform8x8 ? 1 : 0);
          transform8x8 = _engine.DecodeDecision(CtxTransform8x8 + inc) == 1;
        }

        var blocks = transform8x8 ? 4 : 16;
        for (var i = 0; i < blocks; i++)
          _engine.DecodeFlagOrField(CtxPrevIntraPredMode, CtxRemIntraPredMode, RemIntraPredModeBits);

        state.ChromaPredModeNonZero =
          MacroblockReader.ReadChromaPredMode(_engine, left, above) != 0;
        (cbpLuma, cbpChroma) =
          MacroblockReader.ReadCodedBlockPattern(_engine, left, above);
      }
    }
    else if (isB)
    {
      direct16 = interType == BMbTypes.Direct16x16;
      state.Direct = direct16;
      if (!WalkBInterPredictions(mbX, interType,
            out cbpLuma, out cbpChroma, out transform8x8))
      {
        observer?.End(ReconstructionPhase.Header);
        return false;
      }
    }
    else
    {
      if (!WalkInterPredictions(mbX, out cbpLuma, out cbpChroma, out transform8x8))
      {
        observer?.End(ReconstructionPhase.Header);
        return false;
      }
    }

    if (cbpLuma != 0 || cbpChroma != 0 || isIntra16)
      _lastQpDelta = MacroblockReader.ReadQpDelta(_engine, _lastQpDelta);
    else
      _lastQpDelta = 0;
    _currentQp = Math.Clamp(_currentQp + _lastQpDelta, 0, 51);

    observer?.End(ReconstructionPhase.Header);

    state.CbpLuma = cbpLuma;
    state.CbpChroma = cbpChroma;
    state.Transform8x8 = transform8x8;

    if (!WalkResidual(mbX, isIntra, isIntra16, transform8x8, cbpLuma, cbpChroma, ref state))
      return false;

    if (isIntra || direct16) CommitWithoutMotion(mbX, state);
    else Commit(mbX, state);

    var mv = 0;
    if (!isIntra)
    {
      mv = direct16 || !_mbMvdSet
        ? _field.SkipPredictor(fieldIndex)
        : MotionScore.AddMv(_field.SpatialPredictor(fieldIndex), _mbMvd);
      _field.Store(fieldIndex, mv);
    }
    else
    {
      _field.Store(fieldIndex, 0);
    }
    value = ActivityValue(mv, fieldIndex);
    return true;
  }

  private int DecodeBMbType(int ctxInc)
  {
    if (_engine.DecodeDecision(CtxMbTypeB + ctxInc) == 0)
      return BMbTypes.Direct16x16;
    if (_engine.DecodeDecision(CtxMbTypeBSplit) == 0)
      return 1 + _engine.DecodeDecision(CtxMbTypeBBits);

    var bits = _engine.DecodeDecision(CtxMbTypeBWide) << 3;
    bits |= _engine.DecodeDecision(CtxMbTypeBBits) << 2;
    bits |= _engine.DecodeDecision(CtxMbTypeBBits) << 1;
    bits |= _engine.DecodeDecision(CtxMbTypeBBits);

    if (bits < 8) return bits + 3;
    if (bits == 13) return BMbTypes.IntraOffset;
    if (bits == 14) return 11;
    if (bits == 15) return BMbTypes.EightByEight;

    bits = (bits << 1) | _engine.DecodeDecision(CtxMbTypeBBits);
    return bits - 4;
  }

  private int DecodeBSubMbType()
  {
    if (_engine.DecodeDecision(CtxBSubMbType) == 0)
      return BMbTypes.SubDirect;
    if (_engine.DecodeDecision(CtxBSubMbType + 1) == 0)
      return 1 + _engine.DecodeDecision(CtxBSubMbType + 3);

    var subType = 3;
    if (_engine.DecodeDecision(CtxBSubMbType + 2) == 1)
    {
      if (_engine.DecodeDecision(CtxBSubMbType + 3) == 1)
        return 11 + _engine.DecodeDecision(CtxBSubMbType + 3);
      subType += 4;
    }
    subType += 2 * _engine.DecodeDecision(CtxBSubMbType + 3);
    subType += _engine.DecodeDecision(CtxBSubMbType + 3);
    return subType;
  }

  private bool WalkBInterPredictions(
    int mbX, int mbType,
    out int cbpLuma, out int cbpChroma, out bool transform8x8)
  {
    transform8x8 = false;
    var transformEligible = true;

    if (mbType == BMbTypes.Direct16x16)
    {
      transformEligible = _direct8x8Inference;
    }
    else if (mbType == BMbTypes.EightByEight)
    {
      Span<int> subTypes = stackalloc int[4];
      for (var i = 0; i < 4; i++)
      {
        var subType = DecodeBSubMbType();
        subTypes[i] = subType;
        transformEligible &= subType == BMbTypes.SubDirect
          ? _direct8x8Inference
          : BMbTypes.SubPartCellsW[subType] == 2 && BMbTypes.SubPartCellsH[subType] == 2;
      }

      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        var active = list == 0 ? _refIdxL0Active : _refIdxL1Active;
        if (active <= 1) continue;
        for (var i = 0; i < 4; i++)
        {
          var subType = subTypes[i];
          if (subType != BMbTypes.SubDirect && (BMbTypes.SubLists[subType] & flag) != 0)
            DecodeRefIdx(list, mbX, (i & 1) << 1, (i >> 1) << 1, 2, 2);
          else
            ZeroRef(list, mbX, (i & 1) << 1, (i >> 1) << 1, 2, 2);
        }
      }

      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        for (var i = 0; i < 4; i++)
        {
          var subType = subTypes[i];
          var baseX = (i & 1) << 1;
          var baseY = (i >> 1) << 1;
          if (subType == BMbTypes.SubDirect || (BMbTypes.SubLists[subType] & flag) == 0)
          {
            ZeroMvd(list, mbX, baseX, baseY, 2, 2);
            continue;
          }
          var cellsW = BMbTypes.SubPartCellsW[subType];
          var cellsH = BMbTypes.SubPartCellsH[subType];
          for (var py = 0; py < 2 / cellsH; py++)
            for (var px = 0; px < 2 / cellsW; px++)
              DecodeMvdPair(list, mbX, baseX + px * cellsW, baseY + py * cellsH, cellsW, cellsH);
        }
      }
    }
    else if (mbType <= BMbTypes.ListBi)
    {
      for (var list = 0; list < 2; list++)
      {
        var flag = list + 1;
        var active = list == 0 ? _refIdxL0Active : _refIdxL1Active;
        if (active <= 1) continue;
        if ((mbType & flag) != 0)
          DecodeRefIdx(list, mbX, 0, 0, 4, 4);
        else
          ZeroRef(list, mbX, 0, 0, 4, 4);
      }
      for (var list = 0; list < 2; list++)
      {
        if ((mbType & (list + 1)) != 0)
          DecodeMvdPair(list, mbX, 0, 0, 4, 4);
        else
          ZeroMvd(list, mbX, 0, 0, 4, 4);
      }
    }
    else
    {
      var lists = BMbTypes.ListsFor(mbType);
      var vertical = BMbTypes.IsVerticalSplit(mbType);
      var (stepX, stepY) = vertical ? (2, 0) : (0, 2);
      var (cellsW, cellsH) = vertical ? (2, 4) : (4, 2);

      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        var active = list == 0 ? _refIdxL0Active : _refIdxL1Active;
        if (active <= 1) continue;
        for (var part = 0; part < 2; part++)
        {
          if ((lists[part] & flag) != 0)
            DecodeRefIdx(list, mbX, part * stepX, part * stepY, cellsW, cellsH);
          else
            ZeroRef(list, mbX, part * stepX, part * stepY, cellsW, cellsH);
        }
      }

      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        for (var part = 0; part < 2; part++)
        {
          if ((lists[part] & flag) != 0)
            DecodeMvdPair(list, mbX, part * stepX, part * stepY, cellsW, cellsH);
          else
            ZeroMvd(list, mbX, part * stepX, part * stepY, cellsW, cellsH);
        }
      }
    }

    ref var left = ref Left(mbX);
    ref var above = ref _above[mbX];
    (cbpLuma, cbpChroma) = MacroblockReader.ReadCodedBlockPattern(_engine, left, above);

    if (cbpLuma != 0 && _transform8x8Mode && transformEligible)
    {
      var inc = (left.Available && left.Transform8x8 ? 1 : 0)
              + (above.Available && above.Transform8x8 ? 1 : 0);
      transform8x8 = _engine.DecodeDecision(CtxTransform8x8 + inc) == 1;
    }

    return true;
  }

  private bool WalkInterPredictions(
    int mbX, out int cbpLuma, out int cbpChroma, out bool transform8x8)
  {
    transform8x8 = false;

    int mbKind;
    if (_engine.DecodeDecision(CtxMbTypeSplit) == 1)
      mbKind = _engine.DecodeDecision(CtxMbTypeWide) == 1 ? 1 : 2;
    else
      mbKind = _engine.DecodeDecision(CtxMbTypeNarrow) == 1 ? 3 : 0;

    var refActive = _refIdxL0Active;
    var allSub8x8 = true;

    if (mbKind == 3)
    {
      Span<int> subTypes = stackalloc int[4];
      for (var i = 0; i < 4; i++)
      {
        int subType;
        if (_engine.DecodeDecision(CtxSubMbType) == 1)
          subType = 0;
        else if (_engine.DecodeDecision(CtxSubMbType + 1) == 0)
          subType = 1;
        else
          subType = 3 - _engine.DecodeDecision(CtxSubMbType + 2);
        subTypes[i] = subType;
        allSub8x8 &= subType == 0;
      }

      if (refActive > 1)
        for (var i = 0; i < 4; i++)
          DecodeRefIdx(0, mbX, (i & 1) << 1, (i >> 1) << 1, 2, 2);

      for (var i = 0; i < 4; i++)
      {
        var baseX = (i & 1) << 1;
        var baseY = (i >> 1) << 1;
        switch (subTypes[i])
        {
          case 0:
            DecodeMvdPair(0, mbX, baseX, baseY, 2, 2);
            break;
          case 1:
            DecodeMvdPair(0, mbX, baseX, baseY, 2, 1);
            DecodeMvdPair(0, mbX, baseX, baseY + 1, 2, 1);
            break;
          case 2:
            DecodeMvdPair(0, mbX, baseX, baseY, 1, 2);
            DecodeMvdPair(0, mbX, baseX + 1, baseY, 1, 2);
            break;
          default:
            DecodeMvdPair(0, mbX, baseX, baseY, 1, 1);
            DecodeMvdPair(0, mbX, baseX + 1, baseY, 1, 1);
            DecodeMvdPair(0, mbX, baseX, baseY + 1, 1, 1);
            DecodeMvdPair(0, mbX, baseX + 1, baseY + 1, 1, 1);
            break;
        }
      }
    }
    else
    {
      var (partsX, partsY) = mbKind switch { 1 => (1, 2), 2 => (2, 1), _ => (1, 1) };
      var (cellsW, cellsH) = mbKind switch { 1 => (4, 2), 2 => (2, 4), _ => (4, 4) };

      if (refActive > 1)
        for (var py = 0; py < partsY; py++)
          for (var px = 0; px < partsX; px++)
            DecodeRefIdx(0, mbX, px * cellsW, py * cellsH, cellsW, cellsH);

      for (var py = 0; py < partsY; py++)
        for (var px = 0; px < partsX; px++)
          DecodeMvdPair(0, mbX, px * cellsW, py * cellsH, cellsW, cellsH);
    }

    ref var left = ref Left(mbX);
    ref var above = ref _above[mbX];
    (cbpLuma, cbpChroma) = MacroblockReader.ReadCodedBlockPattern(_engine, left, above);

    if (cbpLuma != 0 && _transform8x8Mode && (mbKind != 3 || allSub8x8))
    {
      var inc = (left.Available && left.Transform8x8 ? 1 : 0)
              + (above.Available && above.Transform8x8 ? 1 : 0);
      transform8x8 = _engine.DecodeDecision(CtxTransform8x8 + inc) == 1;
    }

    return true;
  }

  private void DecodeRefIdx(int list, int mbX, int cellX, int cellY, int cellsW, int cellsH)
  {
    var row = _leftRef[list];
    var col = _aboveRef[list];
    var condA = row[cellY] & (cellX > 0 ? -1 : _leftMask);
    var condB = col[mbX * 4 + cellX] & (cellY > 0 ? -1 : _aboveMask);

    var refIdx = 0;
    if (_engine.DecodeDecision(CtxRefIdxFirst + condA + 2 * condB) == 1)
    {
      refIdx = 1;
      if (_engine.DecodeDecision(CtxRefIdxSecond) == 1)
      {
        int bin;
        do
        {
          bin = _engine.DecodeDecision(CtxRefIdxRest);
          refIdx++;
        } while (bin == 1 && refIdx < RefIdxCap);
      }
    }

    var value = (byte)(refIdx != 0 ? 1 : 0);
    for (var y = cellY; y < cellY + cellsH; y++)
      row[y] = value;
    for (var x = cellX; x < cellX + cellsW; x++)
      col[mbX * 4 + x] = value;
  }

  private void ZeroRef(int list, int mbX, int cellX, int cellY, int cellsW, int cellsH)
  {
    var row = _leftRef[list];
    var col = _aboveRef[list];
    for (var y = cellY; y < cellY + cellsH; y++)
      row[y] = 0;
    for (var x = cellX; x < cellX + cellsW; x++)
      col[mbX * 4 + x] = 0;
  }

  private void ZeroMvd(int list, int mbX, int cellX, int cellY, int cellsW, int cellsH)
  {
    var rowX = _leftMvdX[list];
    var rowY = _leftMvdY[list];
    for (var y = cellY; y < cellY + cellsH; y++)
    {
      rowX[y] = 0;
      rowY[y] = 0;
    }

    var colX = _aboveMvdX[list];
    var colY = _aboveMvdY[list];
    for (var x = cellX; x < cellX + cellsW; x++)
    {
      colX[mbX * 4 + x] = 0;
      colY[mbX * 4 + x] = 0;
    }
  }

  private void DecodeMvdPair(int list, int mbX, int cellX, int cellY, int cellsW, int cellsH)
  {
    var x = DecodeMvdComponent(CtxMvdX, _leftMvdX[list], _aboveMvdX[list], mbX, cellX, cellY);
    var y = DecodeMvdComponent(CtxMvdY, _leftMvdY[list], _aboveMvdY[list], mbX, cellX, cellY);

    if (!_mbMvdSet)
    {
      _mbMvd = MotionScore.PackMv(x, y);
      _mbMvdSet = true;
    }

    var absX = (ushort)Math.Abs(x);
    var absY = (ushort)Math.Abs(y);
    var rowX = _leftMvdX[list];
    var rowY = _leftMvdY[list];
    for (var cy = cellY; cy < cellY + cellsH; cy++)
    {
      rowX[cy] = absX;
      rowY[cy] = absY;
    }

    var colX = _aboveMvdX[list];
    var colY = _aboveMvdY[list];
    for (var cx = cellX; cx < cellX + cellsW; cx++)
    {
      colX[mbX * 4 + cx] = absX;
      colY[mbX * 4 + cx] = absY;
    }
  }

  private int DecodeMvdComponent(
    int ctxBase, ushort[] row, ushort[] col, int mbX, int cellX, int cellY)
  {
    var leftAbs = row[cellY] & (cellX > 0 ? -1 : _leftMask);
    var aboveAbs = col[mbX * 4 + cellX] & (cellY > 0 ? -1 : _aboveMask);
    var amvd = leftAbs + aboveAbs;
    var inc = amvd < 3 ? 0 : amvd > 32 ? 2 : 1;

    if (_engine.DecodeDecision(ctxBase + inc) == 0) return 0;

    var magnitude = 1;
    if (_engine.DecodeDecision(ctxBase + 3 + MvdBinCtx[0]) == 1)
    {
      var code = 0;
      var count = 1;
      int bin;
      do
      {
        bin = _engine.DecodeDecision(ctxBase + 3 + MvdBinCtx[count]);
        count++;
        code++;
      } while (bin != 0 && count != MvdPrefixBins);

      if (bin != 0)
        code += DecodeExpGolombBypass(MvdExpGolombOrder) + 1;
      magnitude = code + 1;
    }

    return _engine.DecodeBypass() == 1 ? -magnitude : magnitude;
  }

  private int DecodeExpGolombBypass(int order)
  {
    var prefix = _engine.DecodeBypassUnary(ExpGolombOrderCap - order + 1);
    var width = order + prefix;
    return (((1 << prefix) - 1) << order) + (int)_engine.DecodeBypassBits(width);
  }

  private bool WalkResidual(
    int mbX, bool curIntra, bool isIntra16, bool transform8x8, int cbpLuma, int cbpChroma,
    ref Neighbour state)
  {
    ref var left = ref Left(mbX);
    ref var above = ref _above[mbX];
    var leftCbf = ResolvedCbf(left, curIntra);
    var aboveCbf = ResolvedCbf(above, curIntra);

    if (isIntra16)
    {
      var condA = DirectCbf(left, left.DcCbf, curIntra: true);
      var condB = DirectCbf(above, above.DcCbf, curIntra: true);
      var count = _residuals.ReadActivity(
        _engine, ResidualCategory.LumaDirect, condA, condB, ref _mbActivity);
      state.DcCbf = count > 0;

      if (cbpLuma != 0)
      {
        for (var blk = 0; blk < 16; blk++)
        {
          var (a, b) = LumaCbfConds(blk, state.LumaCbf, leftCbf, aboveCbf);
          count = _residuals.ReadActivity(
            _engine, ResidualCategory.LumaAlternating, a, b, ref _mbActivity);
          if (count > 0) state.LumaCbf |= (ushort)(1 << blk);
        }
      }
    }
    else if (transform8x8)
    {
      for (var quadrant = 0; quadrant < 4; quadrant++)
      {
        if ((cbpLuma & (1 << quadrant)) == 0) continue;
        var count = _residuals.Read8x8Activity(_engine, ref _mbActivity);
        if (count > 0) state.LumaCbf |= (ushort)(0xF << (quadrant * 4));
      }
    }
    else
    {
      for (var blk = 0; blk < 16; blk++)
      {
        if ((cbpLuma & (1 << (blk >> 2))) == 0)
        {
          blk += 3;
          continue;
        }
        var (a, b) = LumaCbfConds(blk, state.LumaCbf, leftCbf, aboveCbf);
        var count = _residuals.ReadActivity(
          _engine, ResidualCategory.Luma, a, b, ref _mbActivity);
        if (count > 0) state.LumaCbf |= (ushort)(1 << blk);
      }
    }

    if (cbpChroma == 0) return true;

    var dcA = DirectCbf(left, left.CbDcCbf, curIntra);
    var dcB = DirectCbf(above, above.CbDcCbf, curIntra);
    var dc = _residuals.ReadActivity(
      _engine, ResidualCategory.ChromaDirect, dcA, dcB, ref _mbChromaActivity);
    state.CbDcCbf = dc > 0;

    dcA = DirectCbf(left, left.CrDcCbf, curIntra);
    dcB = DirectCbf(above, above.CrDcCbf, curIntra);
    dc = _residuals.ReadActivity(
      _engine, ResidualCategory.ChromaDirect, dcA, dcB, ref _mbChromaActivity);
    state.CrDcCbf = dc > 0;

    if (cbpChroma != 2) return true;

    state.CbCbf = WalkChromaAc(left, above, left.CbCbf, above.CbCbf, curIntra);
    state.CrCbf = WalkChromaAc(left, above, left.CrCbf, above.CrCbf, curIntra);
    return true;
  }

  private byte WalkChromaAc(
    in Neighbour left, in Neighbour above, byte leftMask, byte aboveMask, bool curIntra)
  {
    byte mask = 0;
    for (var i = 0; i < 4; i++)
    {
      var bx = i & 1;
      var by = i >> 1;

      var condA = bx > 0
        ? (mask >> (i - 1)) & 1
        : ChromaEdgeCbf(left, leftMask, i + 1, curIntra);
      var condB = by > 0
        ? (mask >> (i - 2)) & 1
        : ChromaEdgeCbf(above, aboveMask, i + 2, curIntra);

      var count = _residuals.ReadActivity(
        _engine, ResidualCategory.ChromaAlternating, condA, condB, ref _mbChromaActivity);
      if (count > 0) mask |= (byte)(1 << i);
    }
    return mask;
  }

  private static int ChromaEdgeCbf(in Neighbour n, byte mask, int bit, bool curIntra)
  {
    if (!n.Available) return curIntra ? 1 : 0;
    if (n.Pcm) return 1;
    return (mask >> bit) & 1;
  }

  private static (int A, int B) LumaCbfConds(
    int block, ushort current, ushort leftCbf, ushort aboveCbf)
  {
    var l = BlockOrder.CbfLeft[block];
    var t = BlockOrder.CbfAbove[block];

    var a = (int)(((l < Outside ? current : leftCbf) >> (l & (Outside - 1))) & 1);
    var b = (int)(((t < Outside ? current : aboveCbf) >> (t & (Outside - 1))) & 1);
    return (a, b);
  }

  private static ushort ResolvedCbf(in Neighbour n, bool curIntra)
  {
    if (!n.Available) return curIntra ? (ushort)0xFFFF : (ushort)0;
    return n.Pcm ? (ushort)0xFFFF : n.LumaCbf;
  }

  private static int DirectCbf(in Neighbour n, bool coded, bool curIntra)
  {
    if (!n.Available) return curIntra ? 1 : 0;
    return n.Pcm || coded ? 1 : 0;
  }

  private bool SkipPcm(int mbX)
  {
    var at = _engine.Suspend();
    if (at + Pcm420Bytes > _engine.BytesTotal)
      return Fail("PCM samples ran past the slice end");
    _engine.Resume(at + Pcm420Bytes);

    CommitWithoutMotion(
      mbX, new Neighbour { Available = true, Pcm = true, CbpLuma = 15, CbpChroma = 2 });
    _lastQpDelta = 0;
    return true;
  }

  private byte ActivityValue(int mv, int fieldIndex)
  {
    var activity = _mbActivity + (_mbChromaActivity >> ChromaActivityShift);
    var visual = MotionScore.VisualActivity(activity, _currentQp);
    return (byte)Math.Min(byte.MaxValue,
      (visual >> SubCellShift) + MotionScore.MvTerm(mv, _field, fieldIndex, 0));
  }

  private void PrepareRows(int widthMbs)
  {
    if (_above.Length < widthMbs)
    {
      _above = new Neighbour[widthMbs];
      _aboveMasks = new sbyte[widthMbs];
      _aboveNotSkipped = new byte[widthMbs];
      for (var list = 0; list < 2; list++)
      {
        _aboveRef[list] = new byte[widthMbs * 4];
        _aboveMvdX[list] = new ushort[widthMbs * 4];
        _aboveMvdY[list] = new ushort[widthMbs * 4];
      }
    }
    Array.Clear(_above);
    Array.Clear(_aboveMasks);
    Array.Clear(_aboveNotSkipped);
    _lastQpDelta = 0;
    ResetLeft();
  }

  private void ResetLeft()
  {
    _leftMask = 0;
    _leftNotSkipped = 0;
  }

  private void CommitWithoutMotion(int mbX, in Neighbour state)
  {
    _above[mbX] = state;
    _aboveMasks[mbX] = 0;
    _leftMask = 0;
    _aboveNotSkipped[mbX] = 1;
    _leftNotSkipped = 1;
  }

  private void Commit(int mbX, in Neighbour state)
  {
    _above[mbX] = state;
    _aboveMasks[mbX] = -1;
    _leftMask = -1;
    _aboveNotSkipped[mbX] = 1;
    _leftNotSkipped = 1;
  }

  private void CommitSkipped(int mbX)
  {
    _above[mbX] = _skipState;
    _aboveMasks[mbX] = 0;
    _leftMask = 0;
    _aboveNotSkipped[mbX] = 0;
    _leftNotSkipped = 0;
    _lastQpDelta = 0;
  }

  private ref Neighbour Left(int mbX)
  {
    if (mbX > 0) return ref _above[mbX - 1];
    return ref _outside;
  }

  private bool Fail(string reason)
  {
    LastFailure = reason;
    return false;
  }
}
