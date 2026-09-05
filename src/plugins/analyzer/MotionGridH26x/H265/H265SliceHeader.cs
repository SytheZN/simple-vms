using System.Numerics;
using static Utils.BitstreamHelpers;

namespace Analyzer.MotionGridH26x;

internal readonly record struct H265SliceHeader
{
  private const byte IdrWRadl = 19;
  private const byte IdrNLp = 20;
  private const uint MaxNumMergeCandBase = 5;
  private const int SliceQpYBase = 26;
  private const int MaxRefIdxActiveMinus1 = 14;
  private const int ChromaWeightsPerEntry = 4;

  public required bool FirstSliceSegmentInPicFlag { get; init; }
  public required bool DependentSliceSegment { get; init; }
  public required uint SliceSegmentAddress { get; init; }
  public required H265SliceType SliceType { get; init; }
  public required int SliceQpY { get; init; }
  public required bool CabacInitFlag { get; init; }
  public required bool SaoLuma { get; init; }
  public required bool SaoChroma { get; init; }
  public required uint MaxNumMergeCand { get; init; }
  public required int NumRefIdxL0 { get; init; }
  public required int NumRefIdxL1 { get; init; }
  public required bool MvdL1Zero { get; init; }
  public required int BitOffsetAfterHeader { get; init; }

  public bool IsIntra => SliceType == H265SliceType.I;

  public static H265SliceHeader? Parse(
    ReadOnlySpan<byte> data, byte nalUnitType, bool firstSliceSegment, int bitOffset,
    H265SpsExtended sps, H265Pps pps)
  {
    var at = bitOffset;

    var dependentSliceSegment = false;
    uint sliceSegmentAddress = 0;
    if (!firstSliceSegment)
    {
      if (pps.DependentSliceSegmentsEnabledFlag)
        dependentSliceSegment = ReadBit(data, ref at);
      sliceSegmentAddress = ReadBits(data, ref at, CeilLog2(sps.PicSizeInCtbsY));
    }

    if (dependentSliceSegment)
      return new H265SliceHeader
      {
        FirstSliceSegmentInPicFlag = firstSliceSegment,
        DependentSliceSegment = true,
        SliceSegmentAddress = sliceSegmentAddress,
        SliceType = H265SliceType.I,
        SliceQpY = 0,
        CabacInitFlag = false,
        SaoLuma = false,
        SaoChroma = false,
        MaxNumMergeCand = 0,
        NumRefIdxL0 = 0,
        NumRefIdxL1 = 0,
        MvdL1Zero = false,
        BitOffsetAfterHeader = at
      };

    Skip(ref at, (int)pps.NumExtraSliceHeaderBits);
    var sliceType = (H265SliceType)ReadExpGolomb(data, ref at);
    if (sliceType is not (H265SliceType.B or H265SliceType.P or H265SliceType.I))
      return null;

    if (pps.OutputFlagPresentFlag)
      Skip(ref at, 1);

    var isIdr = nalUnitType is IdrWRadl or IdrNLp;
    var numUsedByCurr = 0;
    var temporalMvpEnabled = false;

    if (!isIdr)
    {
      Skip(ref at, sps.Log2MaxPicOrderCntLsb);

      var shortTermRefPicSetSpsFlag = ReadBit(data, ref at);
      if (!shortTermRefPicSetSpsFlag)
      {
        var parsed = H265SpsExtended.ParseShortTermRefPicSet(
          data, ref at, sps.NumShortTermRefPicSets, sps.NumShortTermRefPicSets,
          sps.ShortTermNumDeltaPocs);
        if (parsed == null) return null;
        numUsedByCurr = parsed.Value.NumUsedByCurr;
      }
      else if (sps.NumShortTermRefPicSets > 0)
      {
        var idx = sps.NumShortTermRefPicSets > 1
          ? (int)ReadBits(data, ref at, CeilLog2(sps.NumShortTermRefPicSets))
          : 0;
        if (idx >= sps.NumShortTermRefPicSets) return null;
        numUsedByCurr = sps.ShortTermNumUsedByCurr[idx];
      }

      if (sps.LongTermRefPicsPresent)
      {
        var numLongTermSps = 0;
        if (sps.NumLongTermRefPicsSps > 0)
          numLongTermSps = (int)ReadExpGolomb(data, ref at);
        var numLongTermPics = (int)ReadExpGolomb(data, ref at);
        if (numLongTermSps > sps.NumLongTermRefPicsSps || numLongTermPics > 32) return null;

        for (var i = 0; i < numLongTermSps + numLongTermPics; i++)
        {
          if (i < numLongTermSps)
          {
            var ltIdx = 0;
            if (sps.NumLongTermRefPicsSps > 1)
              ltIdx = (int)ReadBits(data, ref at, CeilLog2(sps.NumLongTermRefPicsSps));
            if (ltIdx >= sps.NumLongTermRefPicsSps) return null;
            if (sps.LongTermUsedByCurrSps[ltIdx]) numUsedByCurr++;
          }
          else
          {
            Skip(ref at, sps.Log2MaxPicOrderCntLsb);
            if (ReadBit(data, ref at)) numUsedByCurr++;
          }

          if (ReadBit(data, ref at))
            ReadExpGolomb(data, ref at);
        }
      }

      if (sps.TemporalMvpEnabled)
        temporalMvpEnabled = ReadBit(data, ref at);
    }

    var saoLuma = false;
    var saoChroma = false;
    if (sps.SaoEnabled)
    {
      saoLuma = ReadBit(data, ref at);
      if (sps.ChromaFormatIdc != 0)
        saoChroma = ReadBit(data, ref at);
    }

    var cabacInitFlag = false;
    var mvdL1Zero = false;
    var numRefIdxL0 = 0;
    var numRefIdxL1 = 0;
    uint maxNumMergeCand = 0;

    if (sliceType is H265SliceType.P or H265SliceType.B)
    {
      var l0Minus1 = (int)pps.NumRefIdxL0DefaultActiveMinus1;
      var l1Minus1 = (int)pps.NumRefIdxL1DefaultActiveMinus1;
      if (ReadBit(data, ref at))
      {
        l0Minus1 = (int)ReadExpGolomb(data, ref at);
        if (sliceType == H265SliceType.B)
          l1Minus1 = (int)ReadExpGolomb(data, ref at);
      }
      if (l0Minus1 > MaxRefIdxActiveMinus1 || l1Minus1 > MaxRefIdxActiveMinus1) return null;
      numRefIdxL0 = l0Minus1 + 1;
      numRefIdxL1 = sliceType == H265SliceType.B ? l1Minus1 + 1 : 0;

      if (pps.ListsModificationPresentFlag && numUsedByCurr > 1)
        SkipRefPicListsModification(data, ref at, sliceType, numRefIdxL0, numRefIdxL1, numUsedByCurr);

      if (sliceType == H265SliceType.B)
        mvdL1Zero = ReadBit(data, ref at);

      if (pps.CabacInitPresentFlag)
        cabacInitFlag = ReadBit(data, ref at);

      if (temporalMvpEnabled)
      {
        var collocatedFromL0 = sliceType != H265SliceType.B || ReadBit(data, ref at);
        if ((collocatedFromL0 && numRefIdxL0 > 1) || (!collocatedFromL0 && numRefIdxL1 > 1))
          ReadExpGolomb(data, ref at);
      }

      if ((pps.WeightedPredFlag && sliceType == H265SliceType.P)
          || (pps.WeightedBipredFlag && sliceType == H265SliceType.B))
        SkipPredWeightTable(data, ref at, sps, sliceType, numRefIdxL0, numRefIdxL1);

      var fiveMinusMax = ReadExpGolomb(data, ref at);
      if (fiveMinusMax > MaxNumMergeCandBase - 1) return null;
      maxNumMergeCand = MaxNumMergeCandBase - fiveMinusMax;
    }

    var sliceQpY = SliceQpYBase + pps.InitQpMinus26 + ReadSignedExpGolomb(data, ref at);

    if (pps.PpsSliceChromaQpOffsetsPresentFlag)
    {
      ReadSignedExpGolomb(data, ref at);
      ReadSignedExpGolomb(data, ref at);
    }

    var deblockingDisabled = pps.DeblockingFilterDisabledFlag;
    if (pps.DeblockingFilterOverrideEnabledFlag && ReadBit(data, ref at))
    {
      deblockingDisabled = ReadBit(data, ref at);
      if (!deblockingDisabled)
      {
        ReadSignedExpGolomb(data, ref at);
        ReadSignedExpGolomb(data, ref at);
      }
    }

    if (pps.LoopFilterAcrossSlicesEnabledFlag && (saoLuma || saoChroma || !deblockingDisabled))
      Skip(ref at, 1);

    if (pps.TilesEnabledFlag || pps.EntropyCodingSyncEnabledFlag)
    {
      var entryPoints = (int)ReadExpGolomb(data, ref at);
      if (entryPoints > 0)
      {
        var offsetLength = (int)ReadExpGolomb(data, ref at) + 1;
        if (offsetLength > 32) return null;
        for (var i = 0; i < entryPoints; i++)
          ReadBits(data, ref at, offsetLength);
      }
    }

    if (pps.SliceSegmentHeaderExtensionPresentFlag)
    {
      var extensionBytes = (int)ReadExpGolomb(data, ref at);
      Skip(ref at, extensionBytes * 8);
    }

    at++;

    if (at > data.Length * 8) return null;

    return new H265SliceHeader
    {
      FirstSliceSegmentInPicFlag = firstSliceSegment,
      DependentSliceSegment = false,
      SliceSegmentAddress = sliceSegmentAddress,
      SliceType = sliceType,
      SliceQpY = sliceQpY,
      CabacInitFlag = cabacInitFlag,
      SaoLuma = saoLuma,
      SaoChroma = saoChroma,
      MaxNumMergeCand = maxNumMergeCand,
      NumRefIdxL0 = numRefIdxL0,
      NumRefIdxL1 = numRefIdxL1,
      MvdL1Zero = mvdL1Zero,
      BitOffsetAfterHeader = at
    };
  }

  private static void SkipRefPicListsModification(
    ReadOnlySpan<byte> data, ref int at, H265SliceType sliceType,
    int numRefIdxL0, int numRefIdxL1, int numPicTotalCurr)
  {
    var entryBits = CeilLog2(numPicTotalCurr);

    if (ReadBit(data, ref at))
      for (var i = 0; i < numRefIdxL0; i++)
        ReadBits(data, ref at, entryBits);

    if (sliceType == H265SliceType.B && ReadBit(data, ref at))
      for (var i = 0; i < numRefIdxL1; i++)
        ReadBits(data, ref at, entryBits);
  }

  private static void SkipPredWeightTable(
    ReadOnlySpan<byte> data, ref int at, H265SpsExtended sps, H265SliceType sliceType,
    int numRefIdxL0, int numRefIdxL1)
  {
    ReadExpGolomb(data, ref at);
    var chroma = sps.ChromaFormatIdc != 0;
    if (chroma)
      ReadSignedExpGolomb(data, ref at);

    SkipWeightList(data, ref at, numRefIdxL0, chroma);
    if (sliceType == H265SliceType.B)
      SkipWeightList(data, ref at, numRefIdxL1, chroma);
  }

  private static void SkipWeightList(
    ReadOnlySpan<byte> data, ref int at, int numRefIdx, bool chroma)
  {
    Span<bool> lumaWeight = stackalloc bool[MaxRefIdxActiveMinus1 + 1];
    Span<bool> chromaWeight = stackalloc bool[MaxRefIdxActiveMinus1 + 1];

    for (var i = 0; i < numRefIdx; i++)
      lumaWeight[i] = ReadBit(data, ref at);
    if (chroma)
      for (var i = 0; i < numRefIdx; i++)
        chromaWeight[i] = ReadBit(data, ref at);

    for (var i = 0; i < numRefIdx; i++)
    {
      if (lumaWeight[i])
      {
        ReadSignedExpGolomb(data, ref at);
        ReadSignedExpGolomb(data, ref at);
      }
      if (chromaWeight[i])
        for (var j = 0; j < ChromaWeightsPerEntry; j++)
          ReadSignedExpGolomb(data, ref at);
    }
  }

  private static int CeilLog2(int n) =>
    n <= 1 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)(n - 1));
}
