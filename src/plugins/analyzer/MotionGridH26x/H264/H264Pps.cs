using static Utils.BitstreamHelpers;

namespace Analyzer.MotionGridH26x;

internal sealed record H264Pps
{
  private const int NalHeaderBits = 8;
  private const int WeightedBipredIdcBits = 2;
  private const int SliceGroupChangeDirectionBits = 1;
  private const int SliceGroupMapTypeInterleaved = 0;
  private const int SliceGroupMapTypeDispersed = 2;
  private const int SliceGroupMapTypeChange1 = 3;
  private const int SliceGroupMapTypeChange2 = 4;
  private const int SliceGroupMapTypeChange3 = 5;
  private const int SliceGroupMapTypeExplicit = 6;
  private const int BitsPerByte = 8;

  public required uint PicParameterSetId { get; init; }
  public required uint SeqParameterSetId { get; init; }
  public required bool EntropyCodingModeFlag { get; init; }
  public required bool BottomFieldPicOrderInFramePresentFlag { get; init; }
  public required uint NumSliceGroupsMinus1 { get; init; }
  public required uint NumRefIdxL0DefaultActiveMinus1 { get; init; }
  public required uint NumRefIdxL1DefaultActiveMinus1 { get; init; }
  public required bool WeightedPredFlag { get; init; }
  public required byte WeightedBipredIdc { get; init; }
  public required int PicInitQpMinus26 { get; init; }
  public required int PicInitQsMinus26 { get; init; }
  public required int ChromaQpIndexOffset { get; init; }
  public required bool DeblockingFilterControlPresentFlag { get; init; }
  public required bool ConstrainedIntraPredFlag { get; init; }
  public required bool RedundantPicCntPresentFlag { get; init; }
  public required bool Transform8x8ModeFlag { get; init; }

  public static H264Pps Parse(ReadOnlySpan<byte> rawNal)
  {
    var rbsp = ExtractRbsp(rawNal);
    var bitOffset = 0;
    var data = (ReadOnlySpan<byte>)rbsp;

    Skip(ref bitOffset, NalHeaderBits);
    var picParameterSetId = ReadExpGolomb(data, ref bitOffset);
    var seqParameterSetId = ReadExpGolomb(data, ref bitOffset);
    var entropyCodingModeFlag = ReadBit(data, ref bitOffset);
    var bottomFieldPicOrderInFramePresentFlag = ReadBit(data, ref bitOffset);
    var numSliceGroupsMinus1 = ReadExpGolomb(data, ref bitOffset);

    if (numSliceGroupsMinus1 > 0)
    {
      var sliceGroupMapType = ReadExpGolomb(data, ref bitOffset);
      switch (sliceGroupMapType)
      {
        case SliceGroupMapTypeInterleaved:
          for (uint i = 0; i <= numSliceGroupsMinus1; i++)
            ReadExpGolomb(data, ref bitOffset);
          break;
        case SliceGroupMapTypeDispersed:
          for (uint i = 0; i < numSliceGroupsMinus1; i++)
          {
            ReadExpGolomb(data, ref bitOffset);
            ReadExpGolomb(data, ref bitOffset);
          }
          break;
        case SliceGroupMapTypeChange1:
        case SliceGroupMapTypeChange2:
        case SliceGroupMapTypeChange3:
          Skip(ref bitOffset, SliceGroupChangeDirectionBits);
          ReadExpGolomb(data, ref bitOffset);
          break;
        case SliceGroupMapTypeExplicit:
          var picSize = ReadExpGolomb(data, ref bitOffset) + 1;
          var bits = (int)Math.Ceiling(Math.Log2(numSliceGroupsMinus1 + 1));
          for (uint i = 0; i < picSize; i++)
            Skip(ref bitOffset, bits);
          break;
      }
    }

    var numRefIdxL0DefaultActiveMinus1 = ReadExpGolomb(data, ref bitOffset);
    var numRefIdxL1DefaultActiveMinus1 = ReadExpGolomb(data, ref bitOffset);
    var weightedPredFlag = ReadBit(data, ref bitOffset);
    var weightedBipredIdc = (byte)ReadBits(data, ref bitOffset, WeightedBipredIdcBits);
    var picInitQpMinus26 = ReadSignedExpGolomb(data, ref bitOffset);
    var picInitQsMinus26 = ReadSignedExpGolomb(data, ref bitOffset);
    var chromaQpIndexOffset = ReadSignedExpGolomb(data, ref bitOffset);
    var deblockingFilterControlPresentFlag = ReadBit(data, ref bitOffset);
    var constrainedIntraPredFlag = ReadBit(data, ref bitOffset);
    var redundantPicCntPresentFlag = ReadBit(data, ref bitOffset);

    var transform8x8ModeFlag = false;
    if (MoreRbspData(data, bitOffset, rbsp.Length))
    {
      transform8x8ModeFlag = ReadBit(data, ref bitOffset);
    }

    return new H264Pps
    {
      PicParameterSetId = picParameterSetId,
      SeqParameterSetId = seqParameterSetId,
      EntropyCodingModeFlag = entropyCodingModeFlag,
      BottomFieldPicOrderInFramePresentFlag = bottomFieldPicOrderInFramePresentFlag,
      NumSliceGroupsMinus1 = numSliceGroupsMinus1,
      NumRefIdxL0DefaultActiveMinus1 = numRefIdxL0DefaultActiveMinus1,
      NumRefIdxL1DefaultActiveMinus1 = numRefIdxL1DefaultActiveMinus1,
      WeightedPredFlag = weightedPredFlag,
      WeightedBipredIdc = weightedBipredIdc,
      PicInitQpMinus26 = picInitQpMinus26,
      PicInitQsMinus26 = picInitQsMinus26,
      ChromaQpIndexOffset = chromaQpIndexOffset,
      DeblockingFilterControlPresentFlag = deblockingFilterControlPresentFlag,
      ConstrainedIntraPredFlag = constrainedIntraPredFlag,
      RedundantPicCntPresentFlag = redundantPicCntPresentFlag,
      Transform8x8ModeFlag = transform8x8ModeFlag
    };
  }

  internal static bool MoreRbspData(ReadOnlySpan<byte> data, int bitOffset, int totalBytes)
  {
    var totalBits = totalBytes * BitsPerByte;
    if (bitOffset >= totalBits) return false;
    var lastBit = totalBits - 1;
    while (lastBit > bitOffset)
    {
      var probe = bitOffset;
      if (ReadBitAt(data, lastBit) == 1)
      {
        var any = false;
        for (var b = probe; b < lastBit; b++)
        {
          if (ReadBitAt(data, b) == 1) { any = true; break; }
        }
        return any;
      }
      lastBit--;
    }
    return false;
  }

  private static int ReadBitAt(ReadOnlySpan<byte> data, int bitIndex)
  {
    var byteIndex = bitIndex / BitsPerByte;
    var shift = BitsPerByte - 1 - (bitIndex % BitsPerByte);
    return (data[byteIndex] >> shift) & 1;
  }
}
