using static Utils.BitstreamHelpers;

namespace Analyzer.MotionGridH26x;

internal sealed record H264SpsExtended
{
  private const int NalHeaderBits = 8;
  private const int ProfileIdcBits = 8;
  private const int ConstraintFlagsBits = 8;
  private const int LevelIdcBits = 8;
  private const int QpprimeYZeroTransformBypassBits = 1;
  private const int ChromaFormat444 = 3;
  private const int ScalingListsWithout444 = 8;
  private const int ScalingListsWith444 = 12;
  private const int ScalingListLumaLast = 6;
  private const int ScalingList4x4Coeffs = 16;
  private const int ScalingList8x8Coeffs = 64;
  private const int PicOrderCntTypePocLsb = 0;
  private const int PicOrderCntTypeDelta = 1;
  private const int FrameNumBiasFromMinus4 = 4;
  private const int FieldMultiplier = 2;
  private const int ScalingSeedValue = 8;
  private const int ScalingWrapModulus = 256;

  public required byte ProfileIdc { get; init; }
  public required byte ConstraintSetFlags { get; init; }
  public required byte LevelIdc { get; init; }
  public required uint SeqParameterSetId { get; init; }
  public required byte ChromaArrayType { get; init; }
  public required uint Log2MaxFrameNumMinus4 { get; init; }
  public required uint PicOrderCntType { get; init; }
  public required uint Log2MaxPicOrderCntLsbMinus4 { get; init; }
  public required bool DeltaPicOrderAlwaysZeroFlag { get; init; }
  public required uint NumRefFramesInPicOrderCntCycle { get; init; }
  public required uint MaxNumRefFrames { get; init; }
  public required bool GapsInFrameNumValueAllowedFlag { get; init; }
  public required int PicWidthInMbs { get; init; }
  public required int PicHeightInMapUnits { get; init; }
  public required bool FrameMbsOnlyFlag { get; init; }
  public required bool MbAdaptiveFrameFieldFlag { get; init; }
  public required bool Direct8x8InferenceFlag { get; init; }
  public required bool Separate { get; init; }

  public int FrameNumBits => (int)(Log2MaxFrameNumMinus4 + FrameNumBiasFromMinus4);
  public int PicHeightInMbs => PicHeightInMapUnits * (FrameMbsOnlyFlag ? 1 : FieldMultiplier);
  public int PicSizeInMbs => PicWidthInMbs * PicHeightInMbs;

  private static readonly byte[] ExtendedProfiles =
    [100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134, 135];

  public static H264SpsExtended Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var bitOffset = 0;
    var data = (ReadOnlySpan<byte>)rbsp;

    Skip(ref bitOffset, NalHeaderBits);
    var profileIdc = (byte)ReadBits(data, ref bitOffset, ProfileIdcBits);
    var constraintFlags = (byte)ReadBits(data, ref bitOffset, ConstraintFlagsBits);
    var levelIdc = (byte)ReadBits(data, ref bitOffset, LevelIdcBits);
    var seqParameterSetId = ReadExpGolomb(data, ref bitOffset);

    byte chromaFormatIdc = 1;
    var separateColourPlaneFlag = false;

    if (ExtendedProfiles.AsSpan().Contains(profileIdc))
    {
      chromaFormatIdc = (byte)ReadExpGolomb(data, ref bitOffset);
      if (chromaFormatIdc == ChromaFormat444)
        separateColourPlaneFlag = ReadBit(data, ref bitOffset);
      ReadExpGolomb(data, ref bitOffset);
      ReadExpGolomb(data, ref bitOffset);
      Skip(ref bitOffset, QpprimeYZeroTransformBypassBits);
      var seqScalingMatrixPresent = ReadBit(data, ref bitOffset);
      if (seqScalingMatrixPresent)
      {
        var count = chromaFormatIdc != ChromaFormat444 ? ScalingListsWithout444 : ScalingListsWith444;
        for (var i = 0; i < count; i++)
        {
          if (ReadBit(data, ref bitOffset))
            SkipScalingList(data, ref bitOffset, i < ScalingListLumaLast ? ScalingList4x4Coeffs : ScalingList8x8Coeffs);
        }
      }
    }

    var log2MaxFrameNumMinus4 = ReadExpGolomb(data, ref bitOffset);
    var picOrderCntType = ReadExpGolomb(data, ref bitOffset);
    uint log2MaxPicOrderCntLsbMinus4 = 0;
    var deltaPicOrderAlwaysZeroFlag = false;
    uint numRefFramesInPicOrderCntCycle = 0;
    if (picOrderCntType == PicOrderCntTypePocLsb)
    {
      log2MaxPicOrderCntLsbMinus4 = ReadExpGolomb(data, ref bitOffset);
    }
    else if (picOrderCntType == PicOrderCntTypeDelta)
    {
      deltaPicOrderAlwaysZeroFlag = ReadBit(data, ref bitOffset);
      ReadSignedExpGolomb(data, ref bitOffset);
      ReadSignedExpGolomb(data, ref bitOffset);
      numRefFramesInPicOrderCntCycle = ReadExpGolomb(data, ref bitOffset);
      for (uint i = 0; i < numRefFramesInPicOrderCntCycle; i++)
        ReadSignedExpGolomb(data, ref bitOffset);
    }

    var maxNumRefFrames = ReadExpGolomb(data, ref bitOffset);
    var gapsInFrameNumValueAllowedFlag = ReadBit(data, ref bitOffset);
    var picWidthInMbsMinus1 = ReadExpGolomb(data, ref bitOffset);
    var picHeightInMapUnitsMinus1 = ReadExpGolomb(data, ref bitOffset);
    var frameMbsOnlyFlag = ReadBit(data, ref bitOffset);
    var mbAdaptiveFrameFieldFlag = false;
    if (!frameMbsOnlyFlag)
      mbAdaptiveFrameFieldFlag = ReadBit(data, ref bitOffset);
    var direct8x8InferenceFlag = ReadBit(data, ref bitOffset);

    var chromaArrayType = separateColourPlaneFlag ? (byte)0 : chromaFormatIdc;

    return new H264SpsExtended
    {
      ProfileIdc = profileIdc,
      ConstraintSetFlags = constraintFlags,
      LevelIdc = levelIdc,
      SeqParameterSetId = seqParameterSetId,
      ChromaArrayType = chromaArrayType,
      Log2MaxFrameNumMinus4 = log2MaxFrameNumMinus4,
      PicOrderCntType = picOrderCntType,
      Log2MaxPicOrderCntLsbMinus4 = log2MaxPicOrderCntLsbMinus4,
      DeltaPicOrderAlwaysZeroFlag = deltaPicOrderAlwaysZeroFlag,
      NumRefFramesInPicOrderCntCycle = numRefFramesInPicOrderCntCycle,
      MaxNumRefFrames = maxNumRefFrames,
      GapsInFrameNumValueAllowedFlag = gapsInFrameNumValueAllowedFlag,
      PicWidthInMbs = (int)picWidthInMbsMinus1 + 1,
      PicHeightInMapUnits = (int)picHeightInMapUnitsMinus1 + 1,
      FrameMbsOnlyFlag = frameMbsOnlyFlag,
      MbAdaptiveFrameFieldFlag = mbAdaptiveFrameFieldFlag,
      Direct8x8InferenceFlag = direct8x8InferenceFlag,
      Separate = separateColourPlaneFlag
    };
  }

  private static void SkipScalingList(ReadOnlySpan<byte> data, ref int bitOffset, int size)
  {
    var lastScale = ScalingSeedValue;
    var nextScale = ScalingSeedValue;
    for (var j = 0; j < size; j++)
    {
      if (nextScale != 0)
      {
        var delta = ReadSignedExpGolomb(data, ref bitOffset);
        nextScale = (lastScale + delta + ScalingWrapModulus) % ScalingWrapModulus;
      }
      lastScale = nextScale == 0 ? lastScale : nextScale;
    }
  }
}
