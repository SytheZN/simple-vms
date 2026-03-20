using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class H264SpsParserTests
{
  // Synthetic Baseline profile SPS: 1920x1080
  private static readonly byte[] BaselineSps =
  [
    0x67, 0x42, 0x00, 0x2A, 0x96, 0x35, 0x40, 0xF0,
    0x04, 0x4F, 0xCB, 0x37, 0x01, 0x01, 0x01, 0x40,
    0x00, 0x01, 0xC2, 0x00, 0x00, 0x57, 0xE4, 0x01
  ];

  // Synthetic High profile SPS: 1280x720, chroma=1, 8-bit
  // Bit layout after NAL header (0x67):
  //   profile=100(0x64), compat=0x00, level=31(0x1F)
  //   sps_id=ue(0), chroma=ue(1), depth_luma=ue(0), depth_chroma=ue(0),
  //   qpprime_y_zero=0, scaling_present=0
  //   log2_max_frame_num=ue(0), poc_type=ue(0), log2_max_poc=ue(0),
  //   max_ref_frames=ue(0), gaps=0
  //   width_mbs=ue(79)=80->1280, height_map_units=ue(44)=45->720,
  //   frame_mbs_only=1, direct_8x8=0, crop=0
  private static readonly byte[] HighProfileSps =
  [
    0x67, 0x64, 0x00, 0x1F, 0xAC, 0xF0, 0x14, 0x01,
    0x6C
  ];

  /// <summary>
  /// SCENARIO:
  /// Parse a Baseline profile SPS for 1920x1080
  ///
  /// ACTION:
  /// Parse raw SPS NAL bytes (no start code)
  ///
  /// EXPECTED RESULT:
  /// Profile 66 (Baseline), level 42, dimensions 1920x1080
  /// </summary>
  [Test]
  public void Baseline_ParsesProfileAndDimensions()
  {
    var sps = H264SpsParser.Parse(BaselineSps);

    Assert.That(sps.ProfileIdc, Is.EqualTo(66));
    Assert.That(sps.LevelIdc, Is.EqualTo(42));
    Assert.That(sps.Width, Is.EqualTo(1920));
    Assert.That(sps.Height, Is.EqualTo(1080));
  }

  /// <summary>
  /// SCENARIO:
  /// Baseline profile SPS has default chroma and bit depth
  ///
  /// ACTION:
  /// Parse the Baseline SPS and check chroma/bit depth
  ///
  /// EXPECTED RESULT:
  /// ChromaFormatIdc=1 (4:2:0 default), BitDepthLuma=8, BitDepthChroma=8
  /// </summary>
  [Test]
  public void Baseline_HasDefaultChromaAndBitDepth()
  {
    var sps = H264SpsParser.Parse(BaselineSps);

    Assert.That(sps.ChromaFormatIdc, Is.EqualTo(1));
    Assert.That(sps.BitDepthLuma, Is.EqualTo(8));
    Assert.That(sps.BitDepthChroma, Is.EqualTo(8));
  }

  /// <summary>
  /// SCENARIO:
  /// Profile compatibility byte is preserved
  ///
  /// ACTION:
  /// Parse the Baseline SPS
  ///
  /// EXPECTED RESULT:
  /// ProfileCompatibility matches the raw byte
  /// </summary>
  [Test]
  public void ProfileCompatibility_MatchesRawByte()
  {
    var sps = H264SpsParser.Parse(BaselineSps);

    Assert.That(sps.ProfileCompatibility, Is.EqualTo(BaselineSps[2]));
  }

  /// <summary>
  /// SCENARIO:
  /// Parse a High profile SPS that exercises the extended chroma/bit-depth path
  ///
  /// ACTION:
  /// Parse raw SPS bytes with profile_idc=100
  ///
  /// EXPECTED RESULT:
  /// Profile=100, correct dimensions, chroma=1, 8-bit
  /// </summary>
  [Test]
  public void HighProfile_ParsesExtendedFields()
  {
    var sps = H264SpsParser.Parse(HighProfileSps);

    Assert.That(sps.ProfileIdc, Is.EqualTo(100));
    Assert.That(sps.LevelIdc, Is.EqualTo(31));
    Assert.That(sps.ChromaFormatIdc, Is.EqualTo(1));
    Assert.That(sps.BitDepthLuma, Is.EqualTo(8));
    Assert.That(sps.BitDepthChroma, Is.EqualTo(8));
    Assert.That(sps.Width, Is.EqualTo(1280));
    Assert.That(sps.Height, Is.EqualTo(720));
  }
}
