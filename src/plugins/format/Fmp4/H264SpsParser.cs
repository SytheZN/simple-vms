namespace Format.Fmp4;

public record H264SpsInfo
{
  public required byte ProfileIdc { get; init; }
  public required byte ProfileCompatibility { get; init; }
  public required byte LevelIdc { get; init; }
  public required byte ChromaFormatIdc { get; init; }
  public required byte BitDepthLuma { get; init; }
  public required byte BitDepthChroma { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
}

public static class H264SpsParser
{
  private static readonly byte[] ExtendedProfiles = [100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134];

  public static H264SpsInfo Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = RbspExtractor.Extract(rawNal);
    var reader = new BitReader(rbsp);

    reader.Skip(8);
    var profileIdc = (byte)reader.ReadBits(8);
    var profileCompat = (byte)reader.ReadBits(8);
    var levelIdc = (byte)reader.ReadBits(8);
    reader.ReadExpGolomb();

    byte chromaFormatIdc = 1;
    byte bitDepthLuma = 8;
    byte bitDepthChroma = 8;

    if (ExtendedProfiles.AsSpan().Contains(profileIdc))
    {
      chromaFormatIdc = (byte)reader.ReadExpGolomb();
      if (chromaFormatIdc == 3)
        reader.Skip(1);
      bitDepthLuma = (byte)(reader.ReadExpGolomb() + 8);
      bitDepthChroma = (byte)(reader.ReadExpGolomb() + 8);
      reader.Skip(1);
      var seqScalingMatrixPresent = reader.ReadBit();
      if (seqScalingMatrixPresent)
      {
        var count = chromaFormatIdc != 3 ? 8 : 12;
        for (var i = 0; i < count; i++)
        {
          if (reader.ReadBit())
            SkipScalingList(ref reader, i < 6 ? 16 : 64);
        }
      }
    }

    reader.ReadExpGolomb();
    var picOrderCntType = reader.ReadExpGolomb();
    if (picOrderCntType == 0)
    {
      reader.ReadExpGolomb();
    }
    else if (picOrderCntType == 1)
    {
      reader.Skip(1);
      reader.ReadSignedExpGolomb();
      reader.ReadSignedExpGolomb();
      var numRefFrames = reader.ReadExpGolomb();
      for (uint i = 0; i < numRefFrames; i++)
        reader.ReadSignedExpGolomb();
    }

    reader.ReadExpGolomb();
    reader.Skip(1);

    var picWidthInMbs = (int)reader.ReadExpGolomb() + 1;
    var picHeightInMapUnits = (int)reader.ReadExpGolomb() + 1;
    var frameMbsOnly = reader.ReadBit();
    if (!frameMbsOnly)
      reader.Skip(1);

    reader.Skip(1);

    var cropLeft = 0;
    var cropRight = 0;
    var cropTop = 0;
    var cropBottom = 0;
    var frameCropping = reader.ReadBit();
    if (frameCropping)
    {
      cropLeft = (int)reader.ReadExpGolomb();
      cropRight = (int)reader.ReadExpGolomb();
      cropTop = (int)reader.ReadExpGolomb();
      cropBottom = (int)reader.ReadExpGolomb();
    }

    var subWidthC = chromaFormatIdc == 1 || chromaFormatIdc == 2 ? 2 : 1;
    var subHeightC = chromaFormatIdc == 1 ? 2 : 1;

    var width = picWidthInMbs * 16 - (cropLeft + cropRight) * subWidthC;
    var height = (2 - (frameMbsOnly ? 1 : 0)) * picHeightInMapUnits * 16
      - (cropTop + cropBottom) * subHeightC * (2 - (frameMbsOnly ? 1 : 0));

    return new H264SpsInfo
    {
      ProfileIdc = profileIdc,
      ProfileCompatibility = profileCompat,
      LevelIdc = levelIdc,
      ChromaFormatIdc = chromaFormatIdc,
      BitDepthLuma = bitDepthLuma,
      BitDepthChroma = bitDepthChroma,
      Width = width,
      Height = height
    };
  }

  private static void SkipScalingList(ref BitReader reader, int size)
  {
    var lastScale = 8;
    var nextScale = 8;
    for (var j = 0; j < size; j++)
    {
      if (nextScale != 0)
      {
        var delta = reader.ReadSignedExpGolomb();
        nextScale = (lastScale + delta + 256) % 256;
      }
      lastScale = nextScale == 0 ? lastScale : nextScale;
    }
  }
}
