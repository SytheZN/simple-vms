using Shared.Models.Formats;
using static Shared.Models.Formats.BitstreamHelpers;

namespace Analyzer.Thumbnail;

internal sealed record H265Pps
{
  public required uint Id { get; init; }
  public required uint SpsId { get; init; }
  public required bool DependentSliceSegmentsEnabled { get; init; }
  public required bool OutputFlagPresent { get; init; }
  public required int NumExtraSliceHeaderBits { get; init; }
  public required bool SignDataHiding { get; init; }
  public required bool CabacInitPresent { get; init; }
  public required int InitQp { get; init; }
  public required bool ConstrainedIntraPred { get; init; }
  public required bool TransformSkipEnabled { get; init; }
  public required bool CuQpDeltaEnabled { get; init; }
  public required int DiffCuQpDeltaDepth { get; init; }
  public required int CbQpOffset { get; init; }
  public required int CrQpOffset { get; init; }
  public required bool SliceChromaQpOffsetsPresent { get; init; }
  public required bool WeightedPred { get; init; }
  public required bool WeightedBipred { get; init; }
  public required bool TransquantBypassEnabled { get; init; }
  public required bool TilesEnabled { get; init; }
  public required bool EntropyCodingSyncEnabled { get; init; }
  public required bool DeblockingFilterOverrideEnabled { get; init; }
  public required bool DeblockingFilterDisabled { get; init; }
  public required bool LoopFilterAcrossSlices { get; init; }

  public static H265Pps Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var data = (ReadOnlySpan<byte>)rbsp;
    var at = 16;

    var id = ReadExpGolomb(data, ref at);
    var spsId = ReadExpGolomb(data, ref at);
    var dependentSlices = ReadBit(data, ref at);
    var outputFlagPresent = ReadBit(data, ref at);
    var extraSliceHeaderBits = (int)ReadBits(data, ref at, 3);
    var signDataHiding = ReadBit(data, ref at);
    var cabacInitPresent = ReadBit(data, ref at);

    ReadExpGolomb(data, ref at);
    ReadExpGolomb(data, ref at);

    var initQp = ReadSignedExpGolomb(data, ref at) + 26;
    var constrainedIntraPred = ReadBit(data, ref at);
    var transformSkip = ReadBit(data, ref at);

    var cuQpDeltaEnabled = ReadBit(data, ref at);
    var diffCuQpDeltaDepth = cuQpDeltaEnabled ? (int)ReadExpGolomb(data, ref at) : 0;

    var cbQpOffset = ReadSignedExpGolomb(data, ref at);
    var crQpOffset = ReadSignedExpGolomb(data, ref at);
    var sliceChromaQpOffsets = ReadBit(data, ref at);
    var weightedPred = ReadBit(data, ref at);
    var weightedBipred = ReadBit(data, ref at);
    var transquantBypass = ReadBit(data, ref at);
    var tilesEnabled = ReadBit(data, ref at);
    var entropySync = ReadBit(data, ref at);

    if (tilesEnabled)
      SkipTileConfiguration(data, ref at);

    var loopFilterAcrossSlices = ReadBit(data, ref at);

    var deblockingOverride = false;
    var deblockingDisabled = false;
    if (ReadBit(data, ref at))
    {
      deblockingOverride = ReadBit(data, ref at);
      deblockingDisabled = ReadBit(data, ref at);
      if (!deblockingDisabled)
      {
        ReadSignedExpGolomb(data, ref at);
        ReadSignedExpGolomb(data, ref at);
      }
    }

    if (ReadBit(data, ref at))
      H265Sps.SkipScalingListData(data, ref at);

    return new H265Pps
    {
      Id = id,
      SpsId = spsId,
      DependentSliceSegmentsEnabled = dependentSlices,
      OutputFlagPresent = outputFlagPresent,
      NumExtraSliceHeaderBits = extraSliceHeaderBits,
      SignDataHiding = signDataHiding,
      CabacInitPresent = cabacInitPresent,
      InitQp = initQp,
      ConstrainedIntraPred = constrainedIntraPred,
      TransformSkipEnabled = transformSkip,
      CuQpDeltaEnabled = cuQpDeltaEnabled,
      DiffCuQpDeltaDepth = diffCuQpDeltaDepth,
      CbQpOffset = cbQpOffset,
      CrQpOffset = crQpOffset,
      SliceChromaQpOffsetsPresent = sliceChromaQpOffsets,
      WeightedPred = weightedPred,
      WeightedBipred = weightedBipred,
      TransquantBypassEnabled = transquantBypass,
      TilesEnabled = tilesEnabled,
      EntropyCodingSyncEnabled = entropySync,
      DeblockingFilterOverrideEnabled = deblockingOverride,
      DeblockingFilterDisabled = deblockingDisabled,
      LoopFilterAcrossSlices = loopFilterAcrossSlices
    };
  }

  private static void SkipTileConfiguration(ReadOnlySpan<byte> data, ref int at)
  {
    var columns = (int)ReadExpGolomb(data, ref at) + 1;
    var rows = (int)ReadExpGolomb(data, ref at) + 1;

    if (!ReadBit(data, ref at))
    {
      for (var i = 0; i < columns - 1; i++)
        ReadExpGolomb(data, ref at);
      for (var i = 0; i < rows - 1; i++)
        ReadExpGolomb(data, ref at);
    }

    Skip(ref at, 1);
  }
}
