using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class H265SpsParserTests
{
  // H.265 VPS: Main profile, level 4.1 (123), 1 temporal layer
  // Bit layout after NAL header (40 01):
  //   vps_id=0(4b), base_layer_flags=11(2b), max_layers=0(6b),
  //   max_sub_layers=0(3b), temporal_nesting=1(1b)
  //   PTL: space=0(2b), tier=0(1b), idc=1(5b),
  //   compat=0x60000000(32b), constraints=0(48b), level=123(8b)
  private static readonly byte[] TestVps =
  [
    0x40, 0x01, 0x0C, 0x01, 0x01, 0x60, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7B
  ];

  // H.265 SPS: Main profile, 1920x1080, 4:2:0, 8-bit
  // Bit layout after NAL header (42 01):
  //   sps_vps_id=0(4b), max_sub_layers=0(3b), temporal_nesting=1(1b)
  //   PTL: same as VPS
  //   sps_id=ue(0), chroma=ue(1), width=ue(1920), height=ue(1080),
  //   conf_window=0, depth_luma=ue(0), depth_chroma=ue(0)
  private static readonly byte[] TestSps =
  [
    0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7B, 0xA0,
    0x03, 0xC0, 0x80, 0x10, 0xE5, 0x80
  ];

  /// <summary>
  /// SCENARIO:
  /// Parse a VPS with Main profile
  ///
  /// ACTION:
  /// Parse raw VPS NAL bytes
  ///
  /// EXPECTED RESULT:
  /// Main profile (idc=1), 1 temporal layer, temporal_id_nesting=true
  /// </summary>
  [Test]
  public void Vps_ParsesProfileAndTemporalLayers()
  {
    var vps = H265SpsParser.ParseVps(TestVps);

    Assert.That(vps.Ptl.GeneralProfileIdc, Is.EqualTo(1));
    Assert.That(vps.Ptl.GeneralProfileSpace, Is.EqualTo(0));
    Assert.That(vps.Ptl.GeneralTierFlag, Is.False);
    Assert.That(vps.NumTemporalLayers, Is.EqualTo(1));
    Assert.That(vps.TemporalIdNesting, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Parse an SPS for 1920x1080
  ///
  /// ACTION:
  /// Parse raw SPS NAL bytes
  ///
  /// EXPECTED RESULT:
  /// Dimensions 1920x1080, chroma 4:2:0, 8-bit
  /// </summary>
  [Test]
  public void Sps_ParsesDimensionsAndChroma()
  {
    var sps = H265SpsParser.ParseSps(TestSps);

    Assert.That(sps.Width, Is.EqualTo(1920));
    Assert.That(sps.Height, Is.EqualTo(1080));
    Assert.That(sps.ChromaFormatIdc, Is.EqualTo(1));
    Assert.That(sps.BitDepthLuma, Is.EqualTo(8));
    Assert.That(sps.BitDepthChroma, Is.EqualTo(8));
  }

  /// <summary>
  /// SCENARIO:
  /// SPS profile tier level matches VPS
  ///
  /// ACTION:
  /// Parse SPS and check PTL
  ///
  /// EXPECTED RESULT:
  /// Main profile (idc=1), tier=0
  /// </summary>
  [Test]
  public void Sps_ParsesProfileTierLevel()
  {
    var sps = H265SpsParser.ParseSps(TestSps);

    Assert.That(sps.Ptl.GeneralProfileIdc, Is.EqualTo(1));
    Assert.That(sps.Ptl.GeneralProfileSpace, Is.EqualTo(0));
    Assert.That(sps.Ptl.GeneralTierFlag, Is.False);
    Assert.That(sps.Ptl.GeneralLevelIdc, Is.EqualTo(123));
  }
}
