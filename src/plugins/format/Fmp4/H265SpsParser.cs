namespace Format.Fmp4;

public record H265VpsInfo
{
  public required byte NumTemporalLayers { get; init; }
  public required bool TemporalIdNesting { get; init; }
  public required H265ProfileTierLevel Ptl { get; init; }
}

public record H265SpsInfo
{
  public required byte ChromaFormatIdc { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required byte BitDepthLuma { get; init; }
  public required byte BitDepthChroma { get; init; }
  public required H265ProfileTierLevel Ptl { get; init; }
}

public record H265ProfileTierLevel
{
  public required byte GeneralProfileSpace { get; init; }
  public required bool GeneralTierFlag { get; init; }
  public required byte GeneralProfileIdc { get; init; }
  public required uint GeneralProfileCompatibilityFlags { get; init; }
  public required ulong GeneralConstraintIndicatorFlags { get; init; }
  public required byte GeneralLevelIdc { get; init; }
}

public static class H265SpsParser
{
  public static H265VpsInfo ParseVps(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = RbspExtractor.Extract(rawNal);
    var reader = new BitReader(rbsp);

    reader.Skip(16);
    reader.Skip(4);
    reader.Skip(2);
    reader.Skip(6);
    var maxSubLayersMinus1 = (byte)reader.ReadBits(3);
    var temporalIdNesting = reader.ReadBit();
    var ptl = ReadProfileTierLevel(ref reader, maxSubLayersMinus1);

    return new H265VpsInfo
    {
      NumTemporalLayers = (byte)(maxSubLayersMinus1 + 1),
      TemporalIdNesting = temporalIdNesting,
      Ptl = ptl
    };
  }

  public static H265SpsInfo ParseSps(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = RbspExtractor.Extract(rawNal);
    var reader = new BitReader(rbsp);

    reader.Skip(16);
    reader.Skip(4);
    var maxSubLayersMinus1 = (byte)reader.ReadBits(3);
    reader.Skip(1);
    var ptl = ReadProfileTierLevel(ref reader, maxSubLayersMinus1);

    reader.ReadExpGolomb();
    var chromaFormatIdc = (byte)reader.ReadExpGolomb();
    if (chromaFormatIdc == 3)
      reader.Skip(1);

    var width = (int)reader.ReadExpGolomb();
    var height = (int)reader.ReadExpGolomb();

    var conformanceWindow = reader.ReadBit();
    if (conformanceWindow)
    {
      var cropLeft = (int)reader.ReadExpGolomb();
      var cropRight = (int)reader.ReadExpGolomb();
      var cropTop = (int)reader.ReadExpGolomb();
      var cropBottom = (int)reader.ReadExpGolomb();

      int subWidthC = chromaFormatIdc is 1 or 2 ? 2 : 1;
      int subHeightC = chromaFormatIdc == 1 ? 2 : 1;

      width -= (cropLeft + cropRight) * subWidthC;
      height -= (cropTop + cropBottom) * subHeightC;
    }

    var bitDepthLuma = (byte)(reader.ReadExpGolomb() + 8);
    var bitDepthChroma = (byte)(reader.ReadExpGolomb() + 8);

    return new H265SpsInfo
    {
      ChromaFormatIdc = chromaFormatIdc,
      Width = width,
      Height = height,
      BitDepthLuma = bitDepthLuma,
      BitDepthChroma = bitDepthChroma,
      Ptl = ptl
    };
  }

  private static H265ProfileTierLevel ReadProfileTierLevel(ref BitReader reader, byte maxSubLayersMinus1)
  {
    var generalProfileSpace = (byte)reader.ReadBits(2);
    var generalTierFlag = reader.ReadBit();
    var generalProfileIdc = (byte)reader.ReadBits(5);

    uint profileCompatFlags = 0;
    for (var i = 0; i < 32; i++)
      profileCompatFlags = (profileCompatFlags << 1) | reader.ReadBits(1);

    ulong constraintFlags = 0;
    for (var i = 0; i < 48; i++)
      constraintFlags = (constraintFlags << 1) | reader.ReadBits(1);

    var generalLevelIdc = (byte)reader.ReadBits(8);

    if (maxSubLayersMinus1 > 0)
    {
      var subLayerProfilePresent = new bool[maxSubLayersMinus1];
      var subLayerLevelPresent = new bool[maxSubLayersMinus1];
      for (var i = 0; i < maxSubLayersMinus1; i++)
      {
        subLayerProfilePresent[i] = reader.ReadBit();
        subLayerLevelPresent[i] = reader.ReadBit();
      }
      if (maxSubLayersMinus1 < 8)
        reader.Skip(2 * (8 - maxSubLayersMinus1));
      for (var i = 0; i < maxSubLayersMinus1; i++)
      {
        if (subLayerProfilePresent[i])
          reader.Skip(88);
        if (subLayerLevelPresent[i])
          reader.Skip(8);
      }
    }

    return new H265ProfileTierLevel
    {
      GeneralProfileSpace = generalProfileSpace,
      GeneralTierFlag = generalTierFlag,
      GeneralProfileIdc = generalProfileIdc,
      GeneralProfileCompatibilityFlags = profileCompatFlags,
      GeneralConstraintIndicatorFlags = constraintFlags,
      GeneralLevelIdc = generalLevelIdc
    };
  }
}
