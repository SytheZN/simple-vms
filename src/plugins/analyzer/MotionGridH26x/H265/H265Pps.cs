using static Utils.BitstreamHelpers;

namespace Analyzer.MotionGridH26x;

internal sealed record H265Pps
{
  private const int NalHeaderBits = 16;
  private const int NumExtraSliceHeaderBitsWidth = 3;

  public required uint PpsId { get; init; }
  public required uint SpsId { get; init; }
  public required bool DependentSliceSegmentsEnabledFlag { get; init; }
  public required bool OutputFlagPresentFlag { get; init; }
  public required uint NumExtraSliceHeaderBits { get; init; }
  public required bool SignDataHidingEnabledFlag { get; init; }
  public required bool CabacInitPresentFlag { get; init; }
  public required uint NumRefIdxL0DefaultActiveMinus1 { get; init; }
  public required uint NumRefIdxL1DefaultActiveMinus1 { get; init; }
  public required int InitQpMinus26 { get; init; }
  public required bool TransformSkipEnabledFlag { get; init; }
  public required bool CuQpDeltaEnabledFlag { get; init; }
  public required uint DiffCuQpDeltaDepth { get; init; }
  public required bool PpsSliceChromaQpOffsetsPresentFlag { get; init; }
  public required bool WeightedPredFlag { get; init; }
  public required bool WeightedBipredFlag { get; init; }
  public required bool TransquantBypassEnabledFlag { get; init; }
  public required bool TilesEnabledFlag { get; init; }
  public required bool EntropyCodingSyncEnabledFlag { get; init; }
  public required bool LoopFilterAcrossSlicesEnabledFlag { get; init; }
  public required bool DeblockingFilterOverrideEnabledFlag { get; init; }
  public required bool DeblockingFilterDisabledFlag { get; init; }
  public required bool ListsModificationPresentFlag { get; init; }
  public required bool SliceSegmentHeaderExtensionPresentFlag { get; init; }
  public required bool ExtensionPresentFlag { get; init; }

  public static H265Pps Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var data = (ReadOnlySpan<byte>)rbsp;
    var at = 0;

    Skip(ref at, NalHeaderBits);
    var ppsId = ReadExpGolomb(data, ref at);
    var spsId = ReadExpGolomb(data, ref at);
    var dependentSliceSegments = ReadBit(data, ref at);
    var outputFlagPresent = ReadBit(data, ref at);
    var numExtraSliceHeaderBits = ReadBits(data, ref at, NumExtraSliceHeaderBitsWidth);
    var signDataHiding = ReadBit(data, ref at);
    var cabacInitPresent = ReadBit(data, ref at);
    var numRefIdxL0Default = ReadExpGolomb(data, ref at);
    var numRefIdxL1Default = ReadExpGolomb(data, ref at);
    var initQpMinus26 = ReadSignedExpGolomb(data, ref at);
    Skip(ref at, 1);
    var transformSkipEnabled = ReadBit(data, ref at);
    var cuQpDeltaEnabled = ReadBit(data, ref at);
    uint diffCuQpDeltaDepth = 0;
    if (cuQpDeltaEnabled)
      diffCuQpDeltaDepth = ReadExpGolomb(data, ref at);
    ReadSignedExpGolomb(data, ref at);
    ReadSignedExpGolomb(data, ref at);
    var sliceChromaQpOffsetsPresent = ReadBit(data, ref at);
    var weightedPred = ReadBit(data, ref at);
    var weightedBipred = ReadBit(data, ref at);
    var transquantBypass = ReadBit(data, ref at);
    var tilesEnabled = ReadBit(data, ref at);
    var entropyCodingSync = ReadBit(data, ref at);

    if (tilesEnabled)
    {
      var numTileColumnsMinus1 = (int)ReadExpGolomb(data, ref at);
      var numTileRowsMinus1 = (int)ReadExpGolomb(data, ref at);
      if (!ReadBit(data, ref at))
      {
        for (var i = 0; i < numTileColumnsMinus1; i++)
          ReadExpGolomb(data, ref at);
        for (var i = 0; i < numTileRowsMinus1; i++)
          ReadExpGolomb(data, ref at);
      }
      Skip(ref at, 1);
    }

    var loopFilterAcrossSlices = ReadBit(data, ref at);
    var deblockingControlPresent = ReadBit(data, ref at);
    var deblockingOverrideEnabled = false;
    var deblockingDisabled = false;
    if (deblockingControlPresent)
    {
      deblockingOverrideEnabled = ReadBit(data, ref at);
      deblockingDisabled = ReadBit(data, ref at);
      if (!deblockingDisabled)
      {
        ReadSignedExpGolomb(data, ref at);
        ReadSignedExpGolomb(data, ref at);
      }
    }

    if (ReadBit(data, ref at))
      H265SpsExtended.SkipScalingListData(data, ref at);

    var listsModificationPresent = ReadBit(data, ref at);
    ReadExpGolomb(data, ref at);
    var sliceHeaderExtensionPresent = ReadBit(data, ref at);
    var extensionPresent = ReadBit(data, ref at);

    return new H265Pps
    {
      PpsId = ppsId,
      SpsId = spsId,
      DependentSliceSegmentsEnabledFlag = dependentSliceSegments,
      OutputFlagPresentFlag = outputFlagPresent,
      NumExtraSliceHeaderBits = numExtraSliceHeaderBits,
      SignDataHidingEnabledFlag = signDataHiding,
      CabacInitPresentFlag = cabacInitPresent,
      NumRefIdxL0DefaultActiveMinus1 = numRefIdxL0Default,
      NumRefIdxL1DefaultActiveMinus1 = numRefIdxL1Default,
      InitQpMinus26 = initQpMinus26,
      TransformSkipEnabledFlag = transformSkipEnabled,
      CuQpDeltaEnabledFlag = cuQpDeltaEnabled,
      DiffCuQpDeltaDepth = diffCuQpDeltaDepth,
      PpsSliceChromaQpOffsetsPresentFlag = sliceChromaQpOffsetsPresent,
      WeightedPredFlag = weightedPred,
      WeightedBipredFlag = weightedBipred,
      TransquantBypassEnabledFlag = transquantBypass,
      TilesEnabledFlag = tilesEnabled,
      EntropyCodingSyncEnabledFlag = entropyCodingSync,
      LoopFilterAcrossSlicesEnabledFlag = loopFilterAcrossSlices,
      DeblockingFilterOverrideEnabledFlag = deblockingOverrideEnabled,
      DeblockingFilterDisabledFlag = deblockingDisabled,
      ListsModificationPresentFlag = listsModificationPresent,
      SliceSegmentHeaderExtensionPresentFlag = sliceHeaderExtensionPresent,
      ExtensionPresentFlag = extensionPresent
    };
  }
}
