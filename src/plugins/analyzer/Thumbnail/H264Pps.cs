using static Utils.BitstreamHelpers;

namespace Analyzer.Thumbnail;

internal sealed record H264Pps
{
  public required uint Id { get; init; }
  public required uint SpsId { get; init; }
  public required bool CabacEnabled { get; init; }
  public required bool BottomFieldPicOrderPresent { get; init; }
  public required int InitQp { get; init; }
  public required int ChromaQpIndexOffset { get; init; }
  public required int SecondChromaQpIndexOffset { get; init; }
  public required bool Transform8x8Mode { get; init; }
  public required bool ConstrainedIntraPred { get; init; }
  public required bool DeblockingFilterControlPresent { get; init; }
  public required bool RedundantPicCntPresent { get; init; }

  public required H264ScalingMatrix? ScalingMatrix { get; init; }

  public static H264Pps Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var data = (ReadOnlySpan<byte>)rbsp;
    var at = 0;

    Skip(ref at, 8);
    var id = ReadExpGolomb(data, ref at);
    var spsId = ReadExpGolomb(data, ref at);
    var cabac = ReadBit(data, ref at);
    var bottomFieldPicOrderPresent = ReadBit(data, ref at);

    var sliceGroups = ReadExpGolomb(data, ref at) + 1;
    if (sliceGroups > 1)
      SkipSliceGroupMap(data, ref at, sliceGroups);

    ReadExpGolomb(data, ref at);
    ReadExpGolomb(data, ref at);
    Skip(ref at, 1);
    Skip(ref at, 2);

    var initQp = (int)ReadSignedExpGolomb(data, ref at) + 26;
    ReadSignedExpGolomb(data, ref at);
    var chromaQpIndexOffset = ReadSignedExpGolomb(data, ref at);
    var deblockingControlPresent = ReadBit(data, ref at);
    var constrainedIntraPred = ReadBit(data, ref at);
    var redundantPicCntPresent = ReadBit(data, ref at);

    var transform8x8 = false;
    var secondChromaQpIndexOffset = chromaQpIndexOffset;
    H264ScalingMatrix? scalingMatrix = null;
    if (HasMoreRbspData(data, at))
    {
      transform8x8 = ReadBit(data, ref at);
      if (ReadBit(data, ref at))
        scalingMatrix = H264ScalingMatrix.Read(data, ref at, 6 + (transform8x8 ? 2 : 0));
      secondChromaQpIndexOffset = ReadSignedExpGolomb(data, ref at);
    }

    return new H264Pps
    {
      Id = id,
      SpsId = spsId,
      CabacEnabled = cabac,
      BottomFieldPicOrderPresent = bottomFieldPicOrderPresent,
      InitQp = initQp,
      ChromaQpIndexOffset = chromaQpIndexOffset,
      SecondChromaQpIndexOffset = secondChromaQpIndexOffset,
      Transform8x8Mode = transform8x8,
      ConstrainedIntraPred = constrainedIntraPred,
      DeblockingFilterControlPresent = deblockingControlPresent,
      RedundantPicCntPresent = redundantPicCntPresent,
      ScalingMatrix = scalingMatrix
    };
  }

  private static void SkipSliceGroupMap(ReadOnlySpan<byte> data, ref int at, uint sliceGroups)
  {
    var mapType = ReadExpGolomb(data, ref at);
    switch (mapType)
    {
      case 0:
        for (var i = 0; i < sliceGroups; i++)
          ReadExpGolomb(data, ref at);
        break;
      case 2:
        for (var i = 0; i < sliceGroups - 1; i++)
        {
          ReadExpGolomb(data, ref at);
          ReadExpGolomb(data, ref at);
        }
        break;
      case 3:
      case 4:
      case 5:
        Skip(ref at, 1);
        ReadExpGolomb(data, ref at);
        break;
      case 6:
        var units = ReadExpGolomb(data, ref at) + 1;
        var bits = (int)Math.Ceiling(Math.Log2(sliceGroups));
        for (var i = 0; i < units; i++)
          ReadBits(data, ref at, bits);
        break;
    }
  }

  private static bool HasMoreRbspData(ReadOnlySpan<byte> data, int at)
  {
    var lastBit = (data.Length << 3) - 1;
    while (lastBit > at && PeekBits(data, lastBit, 1) == 0)
      lastBit--;
    return at < lastBit;
  }
}
