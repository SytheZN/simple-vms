using static Utils.BitstreamHelpers;

namespace Analyzer.Thumbnail;

internal sealed record H265Sps
{
  public required uint Id { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }

  public required int CodedWidth { get; init; }
  public required int CodedHeight { get; init; }
  public required byte ChromaFormatIdc { get; init; }
  public required int Log2MaxPicOrderCntLsb { get; init; }
  public required int Log2MinCbSize { get; init; }
  public required int Log2CtbSize { get; init; }
  public required int Log2MinTbSize { get; init; }
  public required int Log2MaxTbSize { get; init; }
  public required int MaxTransformHierarchyDepthIntra { get; init; }
  public required bool ScalingListEnabled { get; init; }
  public required bool SaoEnabled { get; init; }
  public required bool PcmEnabled { get; init; }
  public required bool StrongIntraSmoothing { get; init; }

  public int CtbWidth => (Width + (1 << Log2CtbSize) - 1) >> Log2CtbSize;
  public int CtbHeight => (Height + (1 << Log2CtbSize) - 1) >> Log2CtbSize;

  public static H265Sps? Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var data = (ReadOnlySpan<byte>)rbsp;
    var at = 16;

    Skip(ref at, 4);
    var maxSubLayersMinus1 = (int)ReadBits(data, ref at, 3);
    Skip(ref at, 1);

    SkipProfileTierLevel(data, ref at, maxSubLayersMinus1);

    var id = ReadExpGolomb(data, ref at);
    var chromaFormatIdc = (byte)ReadExpGolomb(data, ref at);
    if (chromaFormatIdc == 3)
      Skip(ref at, 1);

    var codedWidth = (int)ReadExpGolomb(data, ref at);
    var codedHeight = (int)ReadExpGolomb(data, ref at);
    var width = codedWidth;
    var height = codedHeight;

    if (ReadBit(data, ref at))
    {
      var left = (int)ReadExpGolomb(data, ref at);
      var right = (int)ReadExpGolomb(data, ref at);
      var top = (int)ReadExpGolomb(data, ref at);
      var bottom = (int)ReadExpGolomb(data, ref at);

      var subWidth = chromaFormatIdc is 1 or 2 ? 2 : 1;
      var subHeight = chromaFormatIdc == 1 ? 2 : 1;
      width -= subWidth * (left + right);
      height -= subHeight * (top + bottom);
    }

    ReadExpGolomb(data, ref at);
    ReadExpGolomb(data, ref at);
    var log2MaxPocLsb = (int)ReadExpGolomb(data, ref at) + 4;

    var subLayerOrdering = ReadBit(data, ref at);
    for (var i = subLayerOrdering ? 0 : maxSubLayersMinus1; i <= maxSubLayersMinus1; i++)
    {
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
    }

    var log2MinCb = (int)ReadExpGolomb(data, ref at) + 3;
    var log2CtbSize = log2MinCb + (int)ReadExpGolomb(data, ref at);
    var log2MinTb = (int)ReadExpGolomb(data, ref at) + 2;
    var log2MaxTb = log2MinTb + (int)ReadExpGolomb(data, ref at);
    ReadExpGolomb(data, ref at);
    var maxDepthIntra = (int)ReadExpGolomb(data, ref at);

    var scalingListEnabled = ReadBit(data, ref at);
    if (scalingListEnabled && ReadBit(data, ref at))
      SkipScalingListData(data, ref at);

    Skip(ref at, 1);
    var saoEnabled = ReadBit(data, ref at);

    var pcmEnabled = ReadBit(data, ref at);
    if (pcmEnabled)
    {
      Skip(ref at, 8);
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
      Skip(ref at, 1);
    }

    if (log2CtbSize is < 4 or > 6)
      return null;

    var numShortTermRefPicSets = (int)ReadExpGolomb(data, ref at);
    if (numShortTermRefPicSets > 64)
      return null;

    Span<int> deltaPocs = stackalloc int[65];
    for (var i = 0; i < numShortTermRefPicSets; i++)
      deltaPocs[i] = SkipShortTermRefPicSet(data, ref at, i, deltaPocs);

    if (ReadBit(data, ref at))
    {
      var numLongTerm = (int)ReadExpGolomb(data, ref at);
      for (var i = 0; i < numLongTerm; i++)
      {
        Skip(ref at, log2MaxPocLsb);
        Skip(ref at, 1);
      }
    }

    Skip(ref at, 1);
    var strongIntraSmoothing = ReadBit(data, ref at);

    return new H265Sps
    {
      Id = id,
      Width = width,
      Height = height,
      CodedWidth = codedWidth,
      CodedHeight = codedHeight,
      ChromaFormatIdc = chromaFormatIdc,
      Log2MaxPicOrderCntLsb = log2MaxPocLsb,
      Log2MinCbSize = log2MinCb,
      Log2CtbSize = log2CtbSize,
      Log2MinTbSize = log2MinTb,
      Log2MaxTbSize = log2MaxTb,
      MaxTransformHierarchyDepthIntra = maxDepthIntra,
      ScalingListEnabled = scalingListEnabled,
      SaoEnabled = saoEnabled,
      PcmEnabled = pcmEnabled,
      StrongIntraSmoothing = strongIntraSmoothing
    };
  }

  private static int SkipShortTermRefPicSet(
    ReadOnlySpan<byte> data, ref int at, int index, ReadOnlySpan<int> deltaPocs)
  {
    if (index != 0 && ReadBit(data, ref at))
    {
      Skip(ref at, 1);
      ReadExpGolomb(data, ref at);

      var derived = 0;
      for (var j = 0; j <= deltaPocs[index - 1]; j++)
      {
        var used = ReadBit(data, ref at);
        var useDelta = used || ReadBit(data, ref at);
        if (useDelta) derived++;
      }

      return derived;
    }

    var negative = (int)ReadExpGolomb(data, ref at);
    var positive = (int)ReadExpGolomb(data, ref at);

    for (var i = 0; i < negative + positive; i++)
    {
      ReadExpGolomb(data, ref at);
      Skip(ref at, 1);
    }

    return negative + positive;
  }

  private static void SkipProfileTierLevel(
    ReadOnlySpan<byte> data, ref int at, int maxSubLayersMinus1)
  {
    Skip(ref at, 8);
    Skip(ref at, 32);
    Skip(ref at, 48);
    Skip(ref at, 8);

    Span<bool> profilePresent = stackalloc bool[8];
    Span<bool> levelPresent = stackalloc bool[8];
    for (var i = 0; i < maxSubLayersMinus1; i++)
    {
      profilePresent[i] = ReadBit(data, ref at);
      levelPresent[i] = ReadBit(data, ref at);
    }

    if (maxSubLayersMinus1 > 0)
      for (var i = maxSubLayersMinus1; i < 8; i++)
        Skip(ref at, 2);

    for (var i = 0; i < maxSubLayersMinus1; i++)
    {
      if (profilePresent[i])
      {
        Skip(ref at, 8);
        Skip(ref at, 32);
        Skip(ref at, 48);
      }
      if (levelPresent[i])
        Skip(ref at, 8);
    }
  }

  internal static void SkipScalingListData(ReadOnlySpan<byte> data, ref int at)
  {
    for (var sizeId = 0; sizeId < 4; sizeId++)
      for (var matrixId = 0; matrixId < 6; matrixId += sizeId == 3 ? 3 : 1)
      {
        if (!ReadBit(data, ref at))
        {
          ReadExpGolomb(data, ref at);
          continue;
        }

        var coefficients = Math.Min(64, 1 << (4 + (sizeId << 1)));
        if (sizeId > 1)
          ReadSignedExpGolomb(data, ref at);
        for (var i = 0; i < coefficients; i++)
          ReadSignedExpGolomb(data, ref at);
      }
  }
}
