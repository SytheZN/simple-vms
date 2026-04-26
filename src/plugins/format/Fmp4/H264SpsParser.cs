using static Shared.Models.Formats.BitstreamHelpers;

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
    var rbsp = ExtractRbsp(rawNal);
    var bitOffset = 0;
    var data = (ReadOnlySpan<byte>)rbsp;

    Skip(ref bitOffset, 8);
    var profileIdc = (byte)ReadBits(data, ref bitOffset, 8);
    var profileCompat = (byte)ReadBits(data, ref bitOffset, 8);
    var levelIdc = (byte)ReadBits(data, ref bitOffset, 8);
    ReadExpGolomb(data, ref bitOffset);

    byte chromaFormatIdc = 1;
    byte bitDepthLuma = 8;
    byte bitDepthChroma = 8;

    if (ExtendedProfiles.AsSpan().Contains(profileIdc))
    {
      chromaFormatIdc = (byte)ReadExpGolomb(data, ref bitOffset);
      if (chromaFormatIdc == 3)
        Skip(ref bitOffset, 1);
      bitDepthLuma = (byte)(ReadExpGolomb(data, ref bitOffset) + 8);
      bitDepthChroma = (byte)(ReadExpGolomb(data, ref bitOffset) + 8);
      Skip(ref bitOffset, 1);
      var seqScalingMatrixPresent = ReadBit(data, ref bitOffset);
      if (seqScalingMatrixPresent)
      {
        var count = chromaFormatIdc != 3 ? 8 : 12;
        for (var i = 0; i < count; i++)
        {
          if (ReadBit(data, ref bitOffset))
            SkipScalingList(data, ref bitOffset, i < 6 ? 16 : 64);
        }
      }
    }

    ReadExpGolomb(data, ref bitOffset);
    var picOrderCntType = ReadExpGolomb(data, ref bitOffset);
    if (picOrderCntType == 0)
    {
      ReadExpGolomb(data, ref bitOffset);
    }
    else if (picOrderCntType == 1)
    {
      Skip(ref bitOffset, 1);
      ReadSignedExpGolomb(data, ref bitOffset);
      ReadSignedExpGolomb(data, ref bitOffset);
      var numRefFrames = ReadExpGolomb(data, ref bitOffset);
      for (uint i = 0; i < numRefFrames; i++)
        ReadSignedExpGolomb(data, ref bitOffset);
    }

    ReadExpGolomb(data, ref bitOffset);
    Skip(ref bitOffset, 1);

    var picWidthInMbs = (int)ReadExpGolomb(data, ref bitOffset) + 1;
    var picHeightInMapUnits = (int)ReadExpGolomb(data, ref bitOffset) + 1;
    var frameMbsOnly = ReadBit(data, ref bitOffset);
    if (!frameMbsOnly)
      Skip(ref bitOffset, 1);

    Skip(ref bitOffset, 1);

    var cropLeft = 0;
    var cropRight = 0;
    var cropTop = 0;
    var cropBottom = 0;
    var frameCropping = ReadBit(data, ref bitOffset);
    if (frameCropping)
    {
      cropLeft = (int)ReadExpGolomb(data, ref bitOffset);
      cropRight = (int)ReadExpGolomb(data, ref bitOffset);
      cropTop = (int)ReadExpGolomb(data, ref bitOffset);
      cropBottom = (int)ReadExpGolomb(data, ref bitOffset);
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

  private static void SkipScalingList(ReadOnlySpan<byte> data, ref int bitOffset, int size)
  {
    var lastScale = 8;
    var nextScale = 8;
    for (var j = 0; j < size; j++)
    {
      if (nextScale != 0)
      {
        var delta = ReadSignedExpGolomb(data, ref bitOffset);
        nextScale = (lastScale + delta + 256) % 256;
      }
      lastScale = nextScale == 0 ? lastScale : nextScale;
    }
  }
}
