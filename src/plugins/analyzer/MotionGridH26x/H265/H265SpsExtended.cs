using static Utils.BitstreamHelpers;

namespace Analyzer.MotionGridH26x;

internal sealed record H265SpsExtended
{
  private const int NalHeaderBits = 16;
  private const int VpsIdBits = 4;
  private const int MaxSubLayersBits = 3;
  private const int TemporalIdNestingFlagBits = 1;
  private const int ChromaFormat444 = 3;
  private const int Log2MinCbSizeBias = 3;
  private const int Log2MinTbSizeBias = 2;
  private const int Log2MaxPicOrderCntLsbBias = 4;
  private const int PcmSampleBitDepthBits = 8;
  private const int MaxShortTermRefPicSets = 64;
  private const int MaxRefPicsPerList = 16;

  public required uint SpsId { get; init; }
  public required byte ChromaFormatIdc { get; init; }
  public required int PicWidthInLumaSamples { get; init; }
  public required int PicHeightInLumaSamples { get; init; }
  public required int Log2MaxPicOrderCntLsb { get; init; }
  public required int Log2MinCbSize { get; init; }
  public required int Log2CtbSize { get; init; }
  public required int Log2MinTbSize { get; init; }
  public required int Log2MaxTbSize { get; init; }
  public required int MaxTransformDepthInter { get; init; }
  public required int MaxTransformDepthIntra { get; init; }
  public required bool AmpEnabled { get; init; }
  public required bool SaoEnabled { get; init; }
  public required bool PcmEnabled { get; init; }
  public required int NumShortTermRefPicSets { get; init; }
  public required int[] ShortTermNumDeltaPocs { get; init; }
  public required int[] ShortTermNumUsedByCurr { get; init; }
  public required bool LongTermRefPicsPresent { get; init; }
  public required int NumLongTermRefPicsSps { get; init; }
  public required bool[] LongTermUsedByCurrSps { get; init; }
  public required bool TemporalMvpEnabled { get; init; }

  public int MinCbSize => 1 << Log2MinCbSize;
  public int CtbSize => 1 << Log2CtbSize;
  public int PicWidthInMinCb => (PicWidthInLumaSamples + MinCbSize - 1) / MinCbSize;
  public int PicHeightInMinCb => (PicHeightInLumaSamples + MinCbSize - 1) / MinCbSize;
  public int PicWidthInCtbs => (PicWidthInLumaSamples + CtbSize - 1) / CtbSize;
  public int PicHeightInCtbs => (PicHeightInLumaSamples + CtbSize - 1) / CtbSize;
  public int PicSizeInCtbsY => PicWidthInCtbs * PicHeightInCtbs;

  public static H265SpsExtended? Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var data = (ReadOnlySpan<byte>)rbsp;
    var at = 0;

    Skip(ref at, NalHeaderBits);
    Skip(ref at, VpsIdBits);
    var maxSubLayersMinus1 = (int)ReadBits(data, ref at, MaxSubLayersBits);
    Skip(ref at, TemporalIdNestingFlagBits);
    SkipProfileTierLevel(data, ref at, maxSubLayersMinus1);

    var spsId = ReadExpGolomb(data, ref at);
    var chromaFormatIdc = (byte)ReadExpGolomb(data, ref at);
    if (chromaFormatIdc == ChromaFormat444)
      Skip(ref at, 1);

    var picWidth = (int)ReadExpGolomb(data, ref at);
    var picHeight = (int)ReadExpGolomb(data, ref at);

    if (ReadBit(data, ref at))
    {
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
    }

    ReadExpGolomb(data, ref at);
    ReadExpGolomb(data, ref at);
    var log2MaxPocLsb = (int)ReadExpGolomb(data, ref at) + Log2MaxPicOrderCntLsbBias;

    var subLayerOrderingInfoPresent = ReadBit(data, ref at);
    for (var i = subLayerOrderingInfoPresent ? 0 : maxSubLayersMinus1; i <= maxSubLayersMinus1; i++)
    {
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
    }

    var log2MinCb = (int)ReadExpGolomb(data, ref at) + Log2MinCbSizeBias;
    var log2CtbSize = log2MinCb + (int)ReadExpGolomb(data, ref at);
    var log2MinTb = (int)ReadExpGolomb(data, ref at) + Log2MinTbSizeBias;
    var log2MaxTb = log2MinTb + (int)ReadExpGolomb(data, ref at);
    var maxDepthInter = (int)ReadExpGolomb(data, ref at);
    var maxDepthIntra = (int)ReadExpGolomb(data, ref at);

    if (ReadBit(data, ref at) && ReadBit(data, ref at))
      SkipScalingListData(data, ref at);

    var ampEnabled = ReadBit(data, ref at);
    var saoEnabled = ReadBit(data, ref at);

    var pcmEnabled = ReadBit(data, ref at);
    if (pcmEnabled)
    {
      Skip(ref at, PcmSampleBitDepthBits);
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
      Skip(ref at, 1);
    }

    var numShortTermRefPicSets = (int)ReadExpGolomb(data, ref at);
    if (numShortTermRefPicSets > MaxShortTermRefPicSets)
      return null;

    var numDeltaPocs = new int[numShortTermRefPicSets];
    var numUsedByCurr = new int[numShortTermRefPicSets];
    for (var i = 0; i < numShortTermRefPicSets; i++)
    {
      var parsed = ParseShortTermRefPicSet(data, ref at, i, numShortTermRefPicSets, numDeltaPocs);
      if (parsed == null) return null;
      (numDeltaPocs[i], numUsedByCurr[i]) = parsed.Value;
    }

    var longTermPresent = ReadBit(data, ref at);
    var numLongTermSps = 0;
    var longTermUsedSps = Array.Empty<bool>();
    if (longTermPresent)
    {
      numLongTermSps = (int)ReadExpGolomb(data, ref at);
      if (numLongTermSps > MaxRefPicsPerList * 2) return null;
      longTermUsedSps = new bool[numLongTermSps];
      for (var i = 0; i < numLongTermSps; i++)
      {
        Skip(ref at, log2MaxPocLsb);
        longTermUsedSps[i] = ReadBit(data, ref at);
      }
    }

    var temporalMvpEnabled = ReadBit(data, ref at);

    return new H265SpsExtended
    {
      SpsId = spsId,
      ChromaFormatIdc = chromaFormatIdc,
      PicWidthInLumaSamples = picWidth,
      PicHeightInLumaSamples = picHeight,
      Log2MaxPicOrderCntLsb = log2MaxPocLsb,
      Log2MinCbSize = log2MinCb,
      Log2CtbSize = log2CtbSize,
      Log2MinTbSize = log2MinTb,
      Log2MaxTbSize = log2MaxTb,
      MaxTransformDepthInter = maxDepthInter,
      MaxTransformDepthIntra = maxDepthIntra,
      AmpEnabled = ampEnabled,
      SaoEnabled = saoEnabled,
      PcmEnabled = pcmEnabled,
      NumShortTermRefPicSets = numShortTermRefPicSets,
      ShortTermNumDeltaPocs = numDeltaPocs,
      ShortTermNumUsedByCurr = numUsedByCurr,
      LongTermRefPicsPresent = longTermPresent,
      NumLongTermRefPicsSps = numLongTermSps,
      LongTermUsedByCurrSps = longTermUsedSps,
      TemporalMvpEnabled = temporalMvpEnabled
    };
  }

  internal static (int NumDeltaPocs, int NumUsedByCurr)? ParseShortTermRefPicSet(
    ReadOnlySpan<byte> data, ref int at, int stRpsIdx, int numShortTermRefPicSets,
    ReadOnlySpan<int> numDeltaPocs)
  {
    if (stRpsIdx != 0 && ReadBit(data, ref at))
    {
      var deltaIdxMinus1 = 0;
      if (stRpsIdx == numShortTermRefPicSets)
        deltaIdxMinus1 = (int)ReadExpGolomb(data, ref at);

      var refIdx = stRpsIdx - 1 - deltaIdxMinus1;
      if (refIdx < 0 || refIdx >= numDeltaPocs.Length) return null;

      Skip(ref at, 1);
      ReadExpGolomb(data, ref at);

      var derived = 0;
      var used = 0;
      for (var j = 0; j <= numDeltaPocs[refIdx]; j++)
      {
        var usedByCurr = ReadBit(data, ref at);
        var useDelta = usedByCurr || ReadBit(data, ref at);
        if (usedByCurr) used++;
        if (useDelta) derived++;
      }

      return (derived, used);
    }

    var negative = (int)ReadExpGolomb(data, ref at);
    var positive = (int)ReadExpGolomb(data, ref at);
    if (negative > MaxRefPicsPerList || positive > MaxRefPicsPerList) return null;

    var usedCount = 0;
    for (var i = 0; i < negative + positive; i++)
    {
      ReadExpGolomb(data, ref at);
      if (ReadBit(data, ref at)) usedCount++;
    }

    return (negative + positive, usedCount);
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
