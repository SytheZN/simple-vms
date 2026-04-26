using static Shared.Models.Formats.BitstreamHelpers;

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
    var rbsp = ExtractRbsp(rawNal);
    var bitOffset = 0;
    var data = (ReadOnlySpan<byte>)rbsp;

    Skip(ref bitOffset, 16);
    Skip(ref bitOffset, 4);
    Skip(ref bitOffset, 2);
    Skip(ref bitOffset, 6);
    var maxSubLayersMinus1 = (byte)ReadBits(data, ref bitOffset, 3);
    var temporalIdNesting = ReadBit(data, ref bitOffset);
    var ptl = ReadProfileTierLevel(data, ref bitOffset, maxSubLayersMinus1);

    return new H265VpsInfo
    {
      NumTemporalLayers = (byte)(maxSubLayersMinus1 + 1),
      TemporalIdNesting = temporalIdNesting,
      Ptl = ptl
    };
  }

  public static H265SpsInfo ParseSps(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var bitOffset = 0;
    var data = (ReadOnlySpan<byte>)rbsp;

    Skip(ref bitOffset, 16);
    Skip(ref bitOffset, 4);
    var maxSubLayersMinus1 = (byte)ReadBits(data, ref bitOffset, 3);
    Skip(ref bitOffset, 1);
    var ptl = ReadProfileTierLevel(data, ref bitOffset, maxSubLayersMinus1);

    ReadExpGolomb(data, ref bitOffset);
    var chromaFormatIdc = (byte)ReadExpGolomb(data, ref bitOffset);
    if (chromaFormatIdc == 3)
      Skip(ref bitOffset, 1);

    var width = (int)ReadExpGolomb(data, ref bitOffset);
    var height = (int)ReadExpGolomb(data, ref bitOffset);

    var conformanceWindow = ReadBit(data, ref bitOffset);
    if (conformanceWindow)
    {
      var cropLeft = (int)ReadExpGolomb(data, ref bitOffset);
      var cropRight = (int)ReadExpGolomb(data, ref bitOffset);
      var cropTop = (int)ReadExpGolomb(data, ref bitOffset);
      var cropBottom = (int)ReadExpGolomb(data, ref bitOffset);

      int subWidthC = chromaFormatIdc is 1 or 2 ? 2 : 1;
      int subHeightC = chromaFormatIdc == 1 ? 2 : 1;

      width -= (cropLeft + cropRight) * subWidthC;
      height -= (cropTop + cropBottom) * subHeightC;
    }

    var bitDepthLuma = (byte)(ReadExpGolomb(data, ref bitOffset) + 8);
    var bitDepthChroma = (byte)(ReadExpGolomb(data, ref bitOffset) + 8);

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

  private static H265ProfileTierLevel ReadProfileTierLevel(
    ReadOnlySpan<byte> data, ref int bitOffset, byte maxSubLayersMinus1)
  {
    var generalProfileSpace = (byte)ReadBits(data, ref bitOffset, 2);
    var generalTierFlag = ReadBit(data, ref bitOffset);
    var generalProfileIdc = (byte)ReadBits(data, ref bitOffset, 5);

    uint profileCompatFlags = 0;
    for (var i = 0; i < 32; i++)
      profileCompatFlags = (profileCompatFlags << 1) | ReadBits(data, ref bitOffset, 1);

    ulong constraintFlags = 0;
    for (var i = 0; i < 48; i++)
      constraintFlags = (constraintFlags << 1) | ReadBits(data, ref bitOffset, 1);

    var generalLevelIdc = (byte)ReadBits(data, ref bitOffset, 8);

    if (maxSubLayersMinus1 > 0)
    {
      var subLayerProfilePresent = new bool[maxSubLayersMinus1];
      var subLayerLevelPresent = new bool[maxSubLayersMinus1];
      for (var i = 0; i < maxSubLayersMinus1; i++)
      {
        subLayerProfilePresent[i] = ReadBit(data, ref bitOffset);
        subLayerLevelPresent[i] = ReadBit(data, ref bitOffset);
      }
      if (maxSubLayersMinus1 < 8)
        Skip(ref bitOffset, 2 * (8 - maxSubLayersMinus1));
      for (var i = 0; i < maxSubLayersMinus1; i++)
      {
        if (subLayerProfilePresent[i])
          Skip(ref bitOffset, 88);
        if (subLayerLevelPresent[i])
          Skip(ref bitOffset, 8);
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
