using static Utils.BitstreamHelpers;

namespace Analyzer.MotionGridH26x;

internal enum H264SliceType { P, B, I, SP, SI }

internal readonly struct H264SliceHeader
{
  private const int SliceTypeModulo = 5;
  private const int ColourPlaneIdBits = 2;
  private const int NalTypeIdr = 5;
  private const int Log2MaxPicOrderCntLsbBias = 4;
  private const int DirectSpatialMvPredFlagBits = 1;
  private const int SpForSwitchFlagBits = 1;
  private const int WeightedBipredExplicit = 1;
  private const int DisableDeblockingIdc = 1;
  private const int SliceQpYBase = 26;
  private const int ChromaWeightPairs = 2;
  private const int IdrRefPicMarkingBits = 2;
  private const int RefPicOpEnd = 0;
  private const int RefPicOpLongTerm = 5;
  private const int RefPicOpMemMgmtEnd = 3;

  public required uint FirstMbInSlice { get; init; }
  public required H264SliceType SliceType { get; init; }
  public required uint PicParameterSetId { get; init; }
  public required uint FrameNum { get; init; }
  public required bool FieldPicFlag { get; init; }
  public required bool BottomFieldFlag { get; init; }
  public required int SliceQpY { get; init; }
  public required uint CabacInitIdc { get; init; }
  public required uint NumRefIdxL0Active { get; init; }
  public required uint NumRefIdxL1Active { get; init; }
  public required int BitOffsetAfterHeader { get; init; }

  public bool IsIntra => SliceType is H264SliceType.I or H264SliceType.SI;

  public static H264SliceHeader Parse(
    ReadOnlySpan<byte> rbsp, ref int bitOffset, byte nalUnitType, byte nalRefIdc,
    H264SpsExtended sps, H264Pps pps)
  {
    var firstMb = ReadExpGolomb(rbsp, ref bitOffset);
    var sliceTypeRaw = ReadExpGolomb(rbsp, ref bitOffset);
    var sliceType = (H264SliceType)(sliceTypeRaw % SliceTypeModulo);
    var ppsId = ReadExpGolomb(rbsp, ref bitOffset);

    if (sps.Separate)
      Skip(ref bitOffset, ColourPlaneIdBits);

    var frameNum = ReadBits(rbsp, ref bitOffset, sps.FrameNumBits);

    var fieldPicFlag = false;
    var bottomFieldFlag = false;
    if (!sps.FrameMbsOnlyFlag)
    {
      fieldPicFlag = ReadBit(rbsp, ref bitOffset);
      if (fieldPicFlag)
        bottomFieldFlag = ReadBit(rbsp, ref bitOffset);
    }

    var idrPicFlag = nalUnitType == NalTypeIdr;
    if (idrPicFlag)
      ReadExpGolomb(rbsp, ref bitOffset);

    if (sps.PicOrderCntType == 0)
    {
      Skip(ref bitOffset, (int)(sps.Log2MaxPicOrderCntLsbMinus4 + Log2MaxPicOrderCntLsbBias));
      if (pps.BottomFieldPicOrderInFramePresentFlag && !fieldPicFlag)
        ReadSignedExpGolomb(rbsp, ref bitOffset);
    }
    else if (sps.PicOrderCntType == 1 && !sps.DeltaPicOrderAlwaysZeroFlag)
    {
      ReadSignedExpGolomb(rbsp, ref bitOffset);
      if (pps.BottomFieldPicOrderInFramePresentFlag && !fieldPicFlag)
        ReadSignedExpGolomb(rbsp, ref bitOffset);
    }

    if (pps.RedundantPicCntPresentFlag)
      ReadExpGolomb(rbsp, ref bitOffset);

    if (sliceType == H264SliceType.B)
      Skip(ref bitOffset, DirectSpatialMvPredFlagBits);

    var numRefIdxL0 = pps.NumRefIdxL0DefaultActiveMinus1;
    var numRefIdxL1 = pps.NumRefIdxL1DefaultActiveMinus1;
    if (sliceType is H264SliceType.P or H264SliceType.SP or H264SliceType.B)
    {
      var overrideFlag = ReadBit(rbsp, ref bitOffset);
      if (overrideFlag)
      {
        numRefIdxL0 = ReadExpGolomb(rbsp, ref bitOffset);
        if (sliceType == H264SliceType.B)
          numRefIdxL1 = ReadExpGolomb(rbsp, ref bitOffset);
      }
    }

    SkipRefPicListModification(rbsp, ref bitOffset, sliceType, nalUnitType);

    if ((pps.WeightedPredFlag && (sliceType is H264SliceType.P or H264SliceType.SP))
        || (pps.WeightedBipredIdc == WeightedBipredExplicit && sliceType == H264SliceType.B))
    {
      SkipPredWeightTable(rbsp, ref bitOffset, sps, sliceType, numRefIdxL0, numRefIdxL1);
    }

    if (nalRefIdc != 0)
      SkipDecRefPicMarking(rbsp, ref bitOffset, idrPicFlag);

    uint cabacInitIdc = 0;
    if (pps.EntropyCodingModeFlag && sliceType != H264SliceType.I && sliceType != H264SliceType.SI)
      cabacInitIdc = ReadExpGolomb(rbsp, ref bitOffset);

    var sliceQpDelta = ReadSignedExpGolomb(rbsp, ref bitOffset);

    if (sliceType is H264SliceType.SP or H264SliceType.SI)
    {
      if (sliceType == H264SliceType.SP)
        Skip(ref bitOffset, SpForSwitchFlagBits);
      ReadSignedExpGolomb(rbsp, ref bitOffset);
    }

    if (pps.DeblockingFilterControlPresentFlag)
    {
      var disableIdc = ReadExpGolomb(rbsp, ref bitOffset);
      if (disableIdc != DisableDeblockingIdc)
      {
        ReadSignedExpGolomb(rbsp, ref bitOffset);
        ReadSignedExpGolomb(rbsp, ref bitOffset);
      }
    }

    return new H264SliceHeader
    {
      FirstMbInSlice = firstMb,
      SliceType = sliceType,
      PicParameterSetId = ppsId,
      FrameNum = frameNum,
      FieldPicFlag = fieldPicFlag,
      BottomFieldFlag = bottomFieldFlag,
      SliceQpY = SliceQpYBase + pps.PicInitQpMinus26 + sliceQpDelta,
      CabacInitIdc = cabacInitIdc,
      NumRefIdxL0Active = numRefIdxL0 + 1,
      NumRefIdxL1Active = numRefIdxL1 + 1,
      BitOffsetAfterHeader = bitOffset
    };
  }

  private static void SkipRefPicListModification(
    ReadOnlySpan<byte> data, ref int bitOffset, H264SliceType sliceType, byte nalUnitType)
  {
    if (sliceType != H264SliceType.I && sliceType != H264SliceType.SI)
    {
      if (ReadBit(data, ref bitOffset))
        SkipModList(data, ref bitOffset);
    }
    if (sliceType == H264SliceType.B)
    {
      if (ReadBit(data, ref bitOffset))
        SkipModList(data, ref bitOffset);
    }
  }

  private static void SkipModList(ReadOnlySpan<byte> data, ref int bitOffset)
  {
    while (true)
    {
      var op = ReadExpGolomb(data, ref bitOffset);
      if (op == RefPicOpMemMgmtEnd) break;
      ReadExpGolomb(data, ref bitOffset);
    }
  }

  private static void SkipPredWeightTable(
    ReadOnlySpan<byte> data, ref int bitOffset, H264SpsExtended sps,
    H264SliceType sliceType, uint numRefIdxL0, uint numRefIdxL1)
  {
    ReadExpGolomb(data, ref bitOffset);
    if (sps.ChromaArrayType != 0)
      ReadExpGolomb(data, ref bitOffset);
    SkipWeightTableList(data, ref bitOffset, sps, numRefIdxL0);
    if (sliceType == H264SliceType.B)
      SkipWeightTableList(data, ref bitOffset, sps, numRefIdxL1);
  }

  private static void SkipWeightTableList(
    ReadOnlySpan<byte> data, ref int bitOffset, H264SpsExtended sps, uint count)
  {
    for (uint i = 0; i <= count; i++)
    {
      if (ReadBit(data, ref bitOffset))
      {
        ReadSignedExpGolomb(data, ref bitOffset);
        ReadSignedExpGolomb(data, ref bitOffset);
      }
      if (sps.ChromaArrayType != 0 && ReadBit(data, ref bitOffset))
      {
        for (var j = 0; j < ChromaWeightPairs; j++)
        {
          ReadSignedExpGolomb(data, ref bitOffset);
          ReadSignedExpGolomb(data, ref bitOffset);
        }
      }
    }
  }

  private static void SkipDecRefPicMarking(ReadOnlySpan<byte> data, ref int bitOffset, bool idr)
  {
    if (idr)
    {
      Skip(ref bitOffset, IdrRefPicMarkingBits);
      return;
    }
    var adaptive = ReadBit(data, ref bitOffset);
    if (!adaptive) return;
    while (true)
    {
      var op = ReadExpGolomb(data, ref bitOffset);
      if (op == RefPicOpEnd) break;
      if (op is 1 or 3)
        ReadExpGolomb(data, ref bitOffset);
      if (op == 2)
        ReadExpGolomb(data, ref bitOffset);
      if (op is 3 or 6)
        ReadExpGolomb(data, ref bitOffset);
      if (op == 4)
        ReadExpGolomb(data, ref bitOffset);
      if (op == RefPicOpLongTerm)
        break;
    }
  }
}
