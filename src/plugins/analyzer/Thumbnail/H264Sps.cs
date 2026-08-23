using Shared.Models.Formats;
using static Shared.Models.Formats.BitstreamHelpers;

namespace Analyzer.Thumbnail;

internal sealed record H264Sps
{
  public required uint Id { get; init; }
  public required int WidthInMbs { get; init; }
  public required int HeightInMbs { get; init; }
  public required int CroppedWidth { get; init; }
  public required int CroppedHeight { get; init; }
  public required byte ChromaFormatIdc { get; init; }
  public required int Log2MaxFrameNum { get; init; }
  public required uint PicOrderCntType { get; init; }
  public required int Log2MaxPicOrderCntLsb { get; init; }
  public required bool DeltaPicOrderAlwaysZero { get; init; }
  public required bool FrameMbsOnly { get; init; }

  /// <summary>Null when the sequence signals no matrix, which is not the same as a flat one.</summary>
  public required H264ScalingMatrix? ScalingMatrix { get; init; }

  private static readonly byte[] ExtendedProfiles =
    [100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134];

  public static H264Sps Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var data = (ReadOnlySpan<byte>)rbsp;
    var at = 0;

    Skip(ref at, 8);
    var profileIdc = (byte)ReadBits(data, ref at, 8);
    Skip(ref at, 16);
    var id = ReadExpGolomb(data, ref at);

    byte chromaFormatIdc = 1;
    H264ScalingMatrix? scalingMatrix = null;
    if (ExtendedProfiles.AsSpan().Contains(profileIdc))
    {
      chromaFormatIdc = (byte)ReadExpGolomb(data, ref at);
      if (chromaFormatIdc == 3)
        Skip(ref at, 1);
      ReadExpGolomb(data, ref at);
      ReadExpGolomb(data, ref at);
      Skip(ref at, 1);
      if (ReadBit(data, ref at))
        scalingMatrix = H264ScalingMatrix.Read(data, ref at, chromaFormatIdc != 3 ? 8 : 12);
    }

    var log2MaxFrameNum = (int)ReadExpGolomb(data, ref at) + 4;
    var picOrderCntType = ReadExpGolomb(data, ref at);

    var log2MaxPocLsb = 0;
    var deltaPicOrderAlwaysZero = false;
    if (picOrderCntType == 0)
    {
      log2MaxPocLsb = (int)ReadExpGolomb(data, ref at) + 4;
    }
    else if (picOrderCntType == 1)
    {
      deltaPicOrderAlwaysZero = ReadBit(data, ref at);
      ReadSignedExpGolomb(data, ref at);
      ReadSignedExpGolomb(data, ref at);
      var cycleLength = ReadExpGolomb(data, ref at);
      for (var i = 0; i < cycleLength; i++)
        ReadSignedExpGolomb(data, ref at);
    }

    ReadExpGolomb(data, ref at);
    Skip(ref at, 1);

    var widthInMbs = (int)ReadExpGolomb(data, ref at) + 1;
    var heightInMapUnits = (int)ReadExpGolomb(data, ref at) + 1;
    var frameMbsOnly = ReadBit(data, ref at);
    if (!frameMbsOnly)
      Skip(ref at, 1);
    Skip(ref at, 1);

    var heightInMbs = heightInMapUnits * (frameMbsOnly ? 1 : 2);
    var croppedWidth = widthInMbs * 16;
    var croppedHeight = heightInMbs * 16;

    if (ReadBit(data, ref at))
    {
      var left = (int)ReadExpGolomb(data, ref at);
      var right = (int)ReadExpGolomb(data, ref at);
      var top = (int)ReadExpGolomb(data, ref at);
      var bottom = (int)ReadExpGolomb(data, ref at);

      var subWidth = chromaFormatIdc is 1 or 2 ? 2 : 1;
      var subHeight = chromaFormatIdc == 1 ? 2 : 1;
      var unitY = frameMbsOnly ? subHeight : subHeight * 2;

      croppedWidth -= subWidth * (left + right);
      croppedHeight -= unitY * (top + bottom);
    }

    return new H264Sps
    {
      Id = id,
      WidthInMbs = widthInMbs,
      HeightInMbs = heightInMbs,
      CroppedWidth = croppedWidth,
      CroppedHeight = croppedHeight,
      ChromaFormatIdc = chromaFormatIdc,
      Log2MaxFrameNum = log2MaxFrameNum,
      PicOrderCntType = picOrderCntType,
      Log2MaxPicOrderCntLsb = log2MaxPocLsb,
      DeltaPicOrderAlwaysZero = deltaPicOrderAlwaysZero,
      FrameMbsOnly = frameMbsOnly,
      ScalingMatrix = scalingMatrix
    };
  }
}
