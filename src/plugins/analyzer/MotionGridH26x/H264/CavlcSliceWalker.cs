namespace Analyzer.MotionGridH26x;

internal sealed class CavlcSliceWalker
{
  private const byte MbCoded = 255;
  private const int SubCellShift = 2;
  private const int ChromaActivityShift = 2;

  private const int InterMbTypeMax = 5;
  private const int InterMbTypeP8x8Ref0 = 4;
  private const int MaxSubMbTypeP = 3;
  private const int IntraTypeIPcm = 25;
  private const int Intra16x16CbpLumaFirstGroup = 12;
  private const int CbpTableSize = 48;

  private const int RemIntraPredModeBits = 3;
  private const int Intra4x4SubBlocks = 16;
  private const int Intra8x8SubBlocks = 4;
  private const int PcmLumaBytes = 256;
  private const int Pcm420ChromaBytes = 128;

  private const int LumaBlocksPerMb = 16;
  private const int ChromaBlocksPerMb = 4;
  private const int LumaDcCoeffs = 16;
  private const int Luma4x4Coeffs = 16;
  private const int Intra16x16AcCoeffs = 15;
  private const int ChromaDcCoeffs = 4;
  private const int ChromaAcCoeffs = 15;

  private const sbyte Unavailable = -1;
  private const sbyte PcmCoeffs = 16;
  private const byte ChromaFormat420 = 1;

  private static readonly byte[] RightColumnZ = [5, 7, 13, 15];
  private static readonly byte[] BottomRowZ = [10, 11, 14, 15];

  private sbyte[] _aboveLuma = [];
  private sbyte[] _aboveCb = [];
  private sbyte[] _aboveCr = [];
  private readonly sbyte[] _leftLuma = new sbyte[4];
  private readonly sbyte[] _leftCb = new sbyte[2];
  private readonly sbyte[] _leftCr = new sbyte[2];
  private readonly sbyte[] _mbLuma = new sbyte[LumaBlocksPerMb];
  private readonly sbyte[] _mbCb = new sbyte[ChromaBlocksPerMb];
  private readonly sbyte[] _mbCr = new sbyte[ChromaBlocksPerMb];

  private int _mbActivity;
  private int _mbChromaActivity;
  private int _mbMvd;
  private bool _mbMvdSet;
  private int _currentQp;
  private MotionVectorField _field = null!;

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

    if (sps.ChromaArrayType > ChromaFormat420)
      return Fail($"chroma array type {sps.ChromaArrayType} is not supported");

    var widthMbs = sps.PicWidthInMbs;
    PrepareRows(widthMbs);

    _field = field;
    _currentQp = header.SliceQpY;

    var reader = new H264.CavlcReader(rbsp, rbspLength, header.BitOffsetAfterHeader, observer);
    var totalMbs = sps.PicSizeInMbs;
    var mbAddr = (int)header.FirstMbInSlice;
    var mbX = mbAddr % widthMbs;
    var fieldIndex = field.Index(mbX, mbAddr / widthMbs);

    while (mbAddr < totalMbs)
    {
      var skipRun = (int)reader.ReadExpGolomb();
      if (skipRun > 0)
      {
        var run = Math.Min(skipRun, totalMbs - mbAddr);
        var aboveLuma = _aboveLuma;
        var aboveCb = _aboveCb;
        var aboveCr = _aboveCr;
        for (var s = 0; s < run; s++)
        {
          observer?.Block(false);
          var l4 = mbX * 4;
          var c2 = mbX * 2;
          aboveLuma[l4] = 0;
          aboveLuma[l4 + 1] = 0;
          aboveLuma[l4 + 2] = 0;
          aboveLuma[l4 + 3] = 0;
          aboveCb[c2] = 0;
          aboveCb[c2 + 1] = 0;
          aboveCr[c2] = 0;
          aboveCr[c2 + 1] = 0;

          var mv = field.SkipPredictor(fieldIndex);
          if (mv != 0)
          {
            var term = MotionScore.MvTerm(mv, field, fieldIndex, 0);
            if (term != 0)
              cells[mbAddr + s] = (byte)Math.Min(byte.MaxValue, term);
          }
          field.Store(fieldIndex, mv);

          fieldIndex++;
          if (++mbX == widthMbs)
          {
            mbX = 0;
            fieldIndex++;
          }
        }
        mbAddr += run;
        _leftLuma.AsSpan().Fill(0);
        _leftCb.AsSpan().Fill(0);
        _leftCr.AsSpan().Fill(0);
      }
      if (mbAddr >= totalMbs) break;

      observer?.Block(true);
      observer?.Begin(ReconstructionPhase.Header);
      if (!WalkCodedMb(reader, header, sps, pps, mbX, fieldIndex, out var value, observer))
        return false;
      cells[mbAddr] = value;

      if (reader.Exhausted && mbAddr + 1 < totalMbs)
        return Fail($"slice data exhausted at macroblock {mbAddr} of {totalMbs}");
      mbAddr++;
      fieldIndex++;
      if (++mbX == widthMbs)
      {
        mbX = 0;
        fieldIndex++;
      }
    }

    return true;
  }

  private bool WalkCodedMb(
    H264.CavlcReader reader, H264SliceHeader header, H264SpsExtended sps, H264Pps pps,
    int mbX, int fieldIndex, out byte value, IObserverHarness<ReconstructionPhase>? observer)
  {
    value = 0;
    if (mbX == 0) ResetLeft();
    Array.Clear(_mbCb);
    Array.Clear(_mbCr);
    _mbActivity = 0;
    _mbChromaActivity = 0;
    _mbMvd = 0;
    _mbMvdSet = false;

    var mbType = (int)reader.ReadExpGolomb();
    int cbpLuma, cbpChroma;
    var isIntra16 = false;
    var direct16 = false;
    var isB = header.SliceType == H264SliceType.B;
    var intraOffset = isB ? BMbTypes.IntraOffset : InterMbTypeMax;

    if (mbType < intraOffset)
    {
      var walked = isB
        ? WalkBInterPredictions(reader, header, sps, pps, mbType, out cbpLuma, out cbpChroma, out direct16)
        : WalkInterPredictions(reader, header, pps, mbType, out cbpLuma, out cbpChroma);
      if (!walked)
      {
        observer?.End(ReconstructionPhase.Header);
        return false;
      }
    }
    else
    {
      var intraType = mbType - intraOffset;
      if (intraType > IntraTypeIPcm)
      {
        observer?.End(ReconstructionPhase.Header);
        return Fail($"invalid mb_type {mbType}");
      }

      if (intraType == IntraTypeIPcm)
      {
        observer?.End(ReconstructionPhase.Header);
        reader.AlignToByte();
        reader.SkipBytes(
          PcmLumaBytes + (sps.ChromaArrayType == ChromaFormat420 ? Pcm420ChromaBytes : 0));
        if (reader.Exhausted)
          return Fail("PCM samples ran past the slice end");
        CommitUniform(mbX, PcmCoeffs);
        value = MbCoded;
        return true;
      }

      if (intraType == 0)
      {
        var subBlocks = pps.Transform8x8ModeFlag && reader.ReadFlag()
          ? Intra8x8SubBlocks
          : Intra4x4SubBlocks;
        for (var i = 0; i < subBlocks; i++)
        {
          if (!reader.ReadFlag())
            reader.Skip(RemIntraPredModeBits);
        }
        if (sps.ChromaArrayType == ChromaFormat420)
          reader.ReadExpGolomb();

        var cbpIndex = reader.ReadExpGolomb();
        if (cbpIndex >= CbpTableSize)
        {
          observer?.End(ReconstructionPhase.Header);
          return Fail($"invalid intra coded_block_pattern {cbpIndex}");
        }
        var pattern = H264.CavlcTables.Intra4x4CbpTable[cbpIndex];
        cbpLuma = pattern & 0xF;
        cbpChroma = pattern >> 4;
      }
      else
      {
        isIntra16 = true;
        if (sps.ChromaArrayType == ChromaFormat420)
          reader.ReadExpGolomb();
        var group = intraType - 1;
        cbpLuma = group >= Intra16x16CbpLumaFirstGroup ? 0xF : 0;
        cbpChroma = (group >> 2) % 3;
      }
    }

    if (cbpLuma > 0 || cbpChroma > 0 || isIntra16)
      _currentQp = Math.Clamp(_currentQp + reader.ReadSignedExpGolomb(), 0, 51);

    observer?.End(ReconstructionPhase.Header);

    if (!WalkResidual(reader, sps, mbX, isIntra16, cbpLuma, cbpChroma))
      return false;

    Commit(mbX);

    var mv = 0;
    if (mbType < intraOffset)
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

  private bool WalkBInterPredictions(
    H264.CavlcReader reader, H264SliceHeader header, H264SpsExtended sps, H264Pps pps,
    int mbType, out int cbpLuma, out int cbpChroma, out bool direct16)
  {
    cbpLuma = 0;
    cbpChroma = 0;
    direct16 = mbType == BMbTypes.Direct16x16;

    var transformEligible = true;

    if (direct16)
    {
      transformEligible = sps.Direct8x8InferenceFlag;
    }
    else if (mbType == BMbTypes.EightByEight)
    {
      Span<int> subTypes = stackalloc int[4];
      for (var i = 0; i < 4; i++)
      {
        var subType = (int)reader.ReadExpGolomb();
        if (subType >= BMbTypes.SubLists.Length)
          return Fail($"invalid B sub_mb_type {subType}");
        subTypes[i] = subType;
        transformEligible &= subType == BMbTypes.SubDirect
          ? sps.Direct8x8InferenceFlag
          : BMbTypes.SubPartCellsW[subType] == 2 && BMbTypes.SubPartCellsH[subType] == 2;
      }

      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        var active = list == 0 ? header.NumRefIdxL0Active : header.NumRefIdxL1Active;
        if (active <= 1) continue;
        for (var i = 0; i < 4; i++)
        {
          var subType = subTypes[i];
          if (subType != BMbTypes.SubDirect && (BMbTypes.SubLists[subType] & flag) != 0)
            ReadRefIdx(reader, active);
        }
      }

      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        for (var i = 0; i < 4; i++)
        {
          var subType = subTypes[i];
          if (subType == BMbTypes.SubDirect || (BMbTypes.SubLists[subType] & flag) == 0)
            continue;
          var parts = 4 / (BMbTypes.SubPartCellsW[subType] * BMbTypes.SubPartCellsH[subType]);
          for (var p = 0; p < parts; p++)
            ReadMvdPair(reader);
        }
      }
    }
    else if (mbType <= BMbTypes.ListBi)
    {
      var lists = (byte)mbType;
      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        var active = list == 0 ? header.NumRefIdxL0Active : header.NumRefIdxL1Active;
        if (active > 1 && (lists & flag) != 0)
          ReadRefIdx(reader, active);
      }
      for (var list = 0; list < 2; list++)
      {
        if ((lists & (list + 1)) != 0)
          ReadMvdPair(reader);
      }
    }
    else
    {
      var lists = BMbTypes.ListsFor(mbType);
      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        var active = list == 0 ? header.NumRefIdxL0Active : header.NumRefIdxL1Active;
        if (active <= 1) continue;
        for (var part = 0; part < 2; part++)
        {
          if ((lists[part] & flag) != 0)
            ReadRefIdx(reader, active);
        }
      }
      for (var list = 0; list < 2; list++)
      {
        var flag = (byte)(list + 1);
        for (var part = 0; part < 2; part++)
        {
          if ((lists[part] & flag) != 0)
            ReadMvdPair(reader);
        }
      }
    }

    var cbpIndex = reader.ReadExpGolomb();
    if (cbpIndex >= CbpTableSize)
      return Fail($"invalid B coded_block_pattern {cbpIndex}");
    var pattern = H264.CavlcTables.Inter4x4CbpTable[cbpIndex];
    cbpLuma = pattern & 0xF;
    cbpChroma = pattern >> 4;

    if (cbpLuma > 0 && pps.Transform8x8ModeFlag && transformEligible)
      reader.Skip(1);

    return true;
  }

  private void ReadMvdPair(H264.CavlcReader reader)
  {
    var mvx = reader.ReadSignedExpGolomb();
    var mvy = reader.ReadSignedExpGolomb();
    if (_mbMvdSet) return;
    _mbMvd = MotionScore.PackMv(mvx, mvy);
    _mbMvdSet = true;
  }

  private static void ReadRefIdx(H264.CavlcReader reader, uint active)
  {
    if (active == 2) reader.Skip(1);
    else reader.ReadExpGolomb();
  }

  private bool WalkInterPredictions(
    H264.CavlcReader reader, H264SliceHeader header, H264Pps pps, int mbType,
    out int cbpLuma, out int cbpChroma)
  {
    cbpLuma = 0;
    cbpChroma = 0;

    var isSub8x8 = mbType is 3 or InterMbTypeP8x8Ref0;
    var partitions = mbType switch { 0 => 1, 1 or 2 => 2, _ => 4 };
    var allSub8x8 = true;
    Span<int> subMvdPairs = stackalloc int[4];
    subMvdPairs.Fill(1);

    if (isSub8x8)
    {
      for (var i = 0; i < 4; i++)
      {
        var subMbType = reader.ReadExpGolomb();
        if (subMbType > MaxSubMbTypeP)
          return Fail($"invalid sub_mb_type {subMbType}");
        allSub8x8 &= subMbType == 0;
        subMvdPairs[i] = subMbType switch { 0 => 1, 3 => 4, _ => 2 };
      }
    }

    var refCount = mbType == InterMbTypeP8x8Ref0 ? 1u : header.NumRefIdxL0Active;
    if (refCount > 1)
    {
      for (var p = 0; p < partitions; p++)
        ReadRefIdx(reader, refCount);
    }

    for (var p = 0; p < partitions; p++)
    {
      for (var m = 0; m < subMvdPairs[p]; m++)
        ReadMvdPair(reader);
    }

    var cbpIndex = reader.ReadExpGolomb();
    if (cbpIndex >= CbpTableSize)
      return Fail($"invalid inter coded_block_pattern {cbpIndex}");
    var pattern = H264.CavlcTables.Inter4x4CbpTable[cbpIndex];
    cbpLuma = pattern & 0xF;
    cbpChroma = pattern >> 4;

    if (cbpLuma > 0 && pps.Transform8x8ModeFlag && (!isSub8x8 || allSub8x8))
      reader.Skip(1);

    return true;
  }

  private bool WalkResidual(
    H264.CavlcReader reader, H264SpsExtended sps, int mbX,
    bool isIntra16, int cbpLuma, int cbpChroma)
  {
    if (isIntra16)
    {
      if (!WalkLuma(reader, mbX, 0, LumaDcCoeffs, store: false))
        return false;
      for (var blk = 0; blk < LumaBlocksPerMb; blk++)
      {
        if ((cbpLuma & (1 << (blk >> 2))) == 0)
        {
          ZeroLumaQuadrant(blk);
          blk += 3;
          continue;
        }
        if (!WalkLuma(reader, mbX, blk, Intra16x16AcCoeffs, store: true))
          return false;
      }
    }
    else
    {
      for (var blk = 0; blk < LumaBlocksPerMb; blk++)
      {
        if ((cbpLuma & (1 << (blk >> 2))) == 0)
        {
          ZeroLumaQuadrant(blk);
          blk += 3;
          continue;
        }
        if (!WalkLuma(reader, mbX, blk, Luma4x4Coeffs, store: true))
          return false;
      }
    }

    if (sps.ChromaArrayType != ChromaFormat420 || cbpChroma == 0)
      return true;

    for (var c = 0; c < 2; c++)
    {
      var (total, activity) = reader.WalkBlock(0, chromaDirect: true, ChromaDcCoeffs);
      if (total < 0)
        return Fail("implausible chroma DC block");
      _mbChromaActivity += activity;
    }

    if (cbpChroma != 2)
      return true;

    for (var c = 0; c < 2; c++)
    {
      var mb = c == 0 ? _mbCb : _mbCr;
      var left = c == 0 ? _leftCb : _leftCr;
      var above = c == 0 ? _aboveCb : _aboveCr;
      for (var blk = 0; blk < ChromaBlocksPerMb; blk++)
      {
        var nc = ChromaNc(mb, left, above, blk, mbX);
        var (total, activity) = reader.WalkBlock(nc, chromaDirect: false, ChromaAcCoeffs);
        if (total < 0)
          return Fail("implausible chroma AC block");
        mb[blk] = (sbyte)total;
        _mbChromaActivity += activity;
      }
    }

    return true;
  }

  private void ZeroLumaQuadrant(int blk)
  {
    _mbLuma[blk] = 0;
    _mbLuma[blk + 1] = 0;
    _mbLuma[blk + 2] = 0;
    _mbLuma[blk + 3] = 0;
  }

  private bool WalkLuma(H264.CavlcReader reader, int mbX, int blk, int maxCoeff, bool store)
  {
    var (total, activity) = reader.WalkBlock(LumaNc(blk, mbX), chromaDirect: false, maxCoeff);
    if (total < 0)
      return Fail("implausible luma block");
    if (store) _mbLuma[blk] = (sbyte)total;
    _mbActivity += activity;
    return true;
  }

  private int LumaNc(int blk, int mbX)
  {
    var bx = (((blk >> 2) & 1) << 1) | (blk & 1);
    var by = ((blk >> 3) << 1) | ((blk >> 1) & 1);
    int a = bx > 0 ? _mbLuma[ZIndex(bx - 1, by)] : _leftLuma[by];
    int b = by > 0 ? _mbLuma[ZIndex(bx, by - 1)] : _aboveLuma[mbX * 4 + bx];
    return NeighbourAverage(a, b);
  }

  private static int ChromaNc(sbyte[] mb, sbyte[] left, sbyte[] above, int blk, int mbX)
  {
    var bx = blk & 1;
    var by = blk >> 1;
    int a = bx > 0 ? mb[blk - 1] : left[by];
    int b = by > 0 ? mb[blk - 2] : above[mbX * 2 + bx];
    return NeighbourAverage(a, b);
  }

  private static int ZIndex(int bx, int by) =>
    ((by & 2) << 2) | ((bx & 2) << 1) | ((by & 1) << 1) | (bx & 1);

  private static int NeighbourAverage(int a, int b)
  {
    if (a < 0 && b < 0) return 0;
    if (a < 0) return b;
    if (b < 0) return a;
    return (a + b + 1) >> 1;
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
    if (_aboveLuma.Length < widthMbs * 4)
    {
      _aboveLuma = new sbyte[widthMbs * 4];
      _aboveCb = new sbyte[widthMbs * 2];
      _aboveCr = new sbyte[widthMbs * 2];
    }
    _aboveLuma.AsSpan().Fill(Unavailable);
    _aboveCb.AsSpan().Fill(Unavailable);
    _aboveCr.AsSpan().Fill(Unavailable);
    ResetLeft();
  }

  private void ResetLeft()
  {
    _leftLuma.AsSpan().Fill(Unavailable);
    _leftCb.AsSpan().Fill(Unavailable);
    _leftCr.AsSpan().Fill(Unavailable);
  }

  private void Commit(int mbX)
  {
    for (var i = 0; i < 4; i++)
    {
      _leftLuma[i] = _mbLuma[RightColumnZ[i]];
      _aboveLuma[mbX * 4 + i] = _mbLuma[BottomRowZ[i]];
    }
    _leftCb[0] = _mbCb[1];
    _leftCb[1] = _mbCb[3];
    _leftCr[0] = _mbCr[1];
    _leftCr[1] = _mbCr[3];
    _aboveCb[mbX * 2] = _mbCb[2];
    _aboveCb[mbX * 2 + 1] = _mbCb[3];
    _aboveCr[mbX * 2] = _mbCr[2];
    _aboveCr[mbX * 2 + 1] = _mbCr[3];
  }

  private void CommitUniform(int mbX, sbyte count)
  {
    if (mbX == 0) ResetLeft();
    _leftLuma.AsSpan().Fill(count);
    _leftCb.AsSpan().Fill(count);
    _leftCr.AsSpan().Fill(count);
    _aboveLuma.AsSpan(mbX * 4, 4).Fill(count);
    _aboveCb.AsSpan(mbX * 2, 2).Fill(count);
    _aboveCr.AsSpan(mbX * 2, 2).Fill(count);
  }

  private bool Fail(string reason)
  {
    LastFailure = reason;
    return false;
  }
}
