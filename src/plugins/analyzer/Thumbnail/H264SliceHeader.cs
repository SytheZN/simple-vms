using static Utils.BitstreamHelpers;

namespace Analyzer.Thumbnail;

internal sealed record H264SliceHeader
{
  public required uint FirstMbInSlice { get; init; }
  public required uint PpsId { get; init; }
  public required int SliceQp { get; init; }
  public required bool DeblockingFilterDisabled { get; init; }
  public required int BitOffset { get; init; }

  public static H264SliceHeader? Parse(
    ReadOnlySpan<byte> rbsp, byte nalUnitType, byte nalRefIdc, H264Sps sps, H264Pps pps)
  {
    var data = rbsp;
    var at = 8;

    var firstMb = ReadExpGolomb(data, ref at);
    var sliceType = ReadExpGolomb(data, ref at) % 5;
    if (sliceType is not (2 or 4))
      return null;

    var ppsId = ReadExpGolomb(data, ref at);

    Skip(ref at, sps.Log2MaxFrameNum);

    if (!sps.FrameMbsOnly)
    {
      var fieldPic = ReadBit(data, ref at);
      if (fieldPic)
        Skip(ref at, 1);
    }

    var isIdr = nalUnitType == 5;
    if (isIdr)
      ReadExpGolomb(data, ref at);

    if (sps.PicOrderCntType == 0)
    {
      Skip(ref at, sps.Log2MaxPicOrderCntLsb);
      if (pps.BottomFieldPicOrderPresent && sps.FrameMbsOnly)
        ReadSignedExpGolomb(data, ref at);
    }
    else if (sps.PicOrderCntType == 1 && !sps.DeltaPicOrderAlwaysZero)
    {
      ReadSignedExpGolomb(data, ref at);
      if (pps.BottomFieldPicOrderPresent && sps.FrameMbsOnly)
        ReadSignedExpGolomb(data, ref at);
    }

    if (pps.RedundantPicCntPresent)
      ReadExpGolomb(data, ref at);


    if (nalRefIdc != 0)
    {
      if (isIdr)
      {
        Skip(ref at, 1);
        Skip(ref at, 1);
      }
      else if (ReadBit(data, ref at))
      {
        while (true)
        {
          var op = ReadExpGolomb(data, ref at);
          if (op == 0) break;
          if (op is 1 or 3) ReadExpGolomb(data, ref at);
          if (op == 2) ReadExpGolomb(data, ref at);
          if (op is 3 or 6) ReadExpGolomb(data, ref at);
          if (op == 4) ReadExpGolomb(data, ref at);
        }
      }
    }

    var sliceQpDelta = ReadSignedExpGolomb(data, ref at);
    var sliceQp = pps.InitQp + sliceQpDelta;

    var deblockingDisabled = false;
    if (pps.DeblockingFilterControlPresent)
    {
      var disableIdc = ReadExpGolomb(data, ref at);
      deblockingDisabled = disableIdc == 1;
      if (!deblockingDisabled)
      {
        ReadSignedExpGolomb(data, ref at);
        ReadSignedExpGolomb(data, ref at);
      }
    }

    return new H264SliceHeader
    {
      FirstMbInSlice = firstMb,
      PpsId = ppsId,
      SliceQp = sliceQp,
      DeblockingFilterDisabled = deblockingDisabled,
      BitOffset = at
    };
  }
}
