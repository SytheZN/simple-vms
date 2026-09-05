using static Utils.BitstreamHelpers;

namespace Analyzer.Thumbnail;

internal sealed record H265SliceHeader
{
  public required uint PpsId { get; init; }
  public required int SliceQp { get; init; }
  public required bool SaoLuma { get; init; }
  public required bool SaoChroma { get; init; }
  public required int BitOffset { get; init; }

  public static H265SliceHeader? Parse(
    ReadOnlySpan<byte> rbsp, byte nalUnitType, H265Sps sps, H265Pps pps)
  {
    var data = rbsp;
    var at = 16;

    var firstSliceInPic = ReadBit(data, ref at);
    if (!firstSliceInPic)
      return null;

    if (nalUnitType is >= 16 and <= 23)
      Skip(ref at, 1);

    var ppsId = ReadExpGolomb(data, ref at);
    if (ppsId != pps.Id)
      return null;

    Skip(ref at, pps.NumExtraSliceHeaderBits);

    var sliceType = ReadExpGolomb(data, ref at);
    if (sliceType != 2)
      return null;

    if (pps.OutputFlagPresent)
      Skip(ref at, 1);

    var isIdr = nalUnitType is 19 or 20;
    if (!isIdr)
    {
      Skip(ref at, sps.Log2MaxPicOrderCntLsb);
      return null;
    }

    var saoLuma = false;
    var saoChroma = false;
    if (sps.SaoEnabled)
    {
      saoLuma = ReadBit(data, ref at);
      saoChroma = ReadBit(data, ref at);
    }

    var sliceQp = pps.InitQp + ReadSignedExpGolomb(data, ref at);

    if (pps.SliceChromaQpOffsetsPresent)
    {
      ReadSignedExpGolomb(data, ref at);
      ReadSignedExpGolomb(data, ref at);
    }

    var deblockingDisabled = pps.DeblockingFilterDisabled;
    if (pps.DeblockingFilterOverrideEnabled && ReadBit(data, ref at))
    {
      deblockingDisabled = ReadBit(data, ref at);
      if (!deblockingDisabled)
      {
        ReadSignedExpGolomb(data, ref at);
        ReadSignedExpGolomb(data, ref at);
      }
    }

    if (pps.LoopFilterAcrossSlices && (saoLuma || saoChroma || !deblockingDisabled))
      Skip(ref at, 1);

    if (pps.TilesEnabled || pps.EntropyCodingSyncEnabled)
    {
      var entryPoints = ReadExpGolomb(data, ref at);
      if (entryPoints > 0)
      {
        var offsetLength = (int)ReadExpGolomb(data, ref at) + 1;
        for (var i = 0; i < entryPoints; i++)
          ReadBits(data, ref at, offsetLength);
      }
    }

    at++;

    return new H265SliceHeader
    {
      PpsId = ppsId,
      SliceQp = sliceQp,
      SaoLuma = saoLuma,
      SaoChroma = saoChroma,
      BitOffset = at
    };
  }
}
