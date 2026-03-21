using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class HvcCBuilderTests
{
  private static readonly byte[] TestVps =
  [
    0x40, 0x01, 0x0C, 0x01, 0x01, 0x60, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7B
  ];

  private static readonly byte[] TestSps =
  [
    0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7B, 0xA0,
    0x01, 0xE0, 0x20, 0x02, 0x1C, 0x4D, 0x94, 0x00
  ];

  private static readonly byte[] TestPps = [0x44, 0x01, 0xC1, 0x72, 0xB4, 0x62, 0x40];

  /// <summary>
  /// SCENARIO:
  /// Build an hvcC box from VPS/SPS/PPS
  ///
  /// ACTION:
  /// Parse VPS/SPS, build hvcC
  ///
  /// EXPECTED RESULT:
  /// Starts with configurationVersion=1, contains profile_idc=1,
  /// lengthSizeMinusOne=3, has 3 NAL arrays
  /// </summary>
  [Test]
  public void Build_HasCorrectHeader()
  {
    var vpsInfo = H265SpsParser.ParseVps(TestVps);
    var spsInfo = H265SpsParser.ParseSps(TestSps);
    var hvcC = HvcCBuilder.Build(TestVps, TestSps, TestPps, vpsInfo, spsInfo);

    Assert.That(hvcC[0], Is.EqualTo(1));

    var profileByte = hvcC[1];
    var profileIdc = profileByte & 0x1F;
    Assert.That(profileIdc, Is.EqualTo(1));

    var lengthSizeMinusOne = hvcC[21] & 0x03;
    Assert.That(lengthSizeMinusOne, Is.EqualTo(3));

    Assert.That(hvcC[22], Is.EqualTo(3));
  }

  /// <summary>
  /// SCENARIO:
  /// hvcC contains all three NAL arrays (VPS, SPS, PPS)
  ///
  /// ACTION:
  /// Build hvcC, locate the three NAL arrays
  ///
  /// EXPECTED RESULT:
  /// Array types are 32 (VPS), 33 (SPS), 34 (PPS), each with count=1
  /// </summary>
  [Test]
  public void Build_ContainsAllNalArrays()
  {
    var vpsInfo = H265SpsParser.ParseVps(TestVps);
    var spsInfo = H265SpsParser.ParseSps(TestSps);
    var hvcC = HvcCBuilder.Build(TestVps, TestSps, TestPps, vpsInfo, spsInfo);

    var offset = 23;

    Assert.That(hvcC[offset], Is.EqualTo(32));
    var vpsCount = (hvcC[offset + 1] << 8) | hvcC[offset + 2];
    Assert.That(vpsCount, Is.EqualTo(1));
    var vpsLen = (hvcC[offset + 3] << 8) | hvcC[offset + 4];
    Assert.That(vpsLen, Is.EqualTo(TestVps.Length));
    offset += 5 + vpsLen;

    Assert.That(hvcC[offset], Is.EqualTo(33));
    var spsCount = (hvcC[offset + 1] << 8) | hvcC[offset + 2];
    Assert.That(spsCount, Is.EqualTo(1));
    var spsLen = (hvcC[offset + 3] << 8) | hvcC[offset + 4];
    Assert.That(spsLen, Is.EqualTo(TestSps.Length));
    offset += 5 + spsLen;

    Assert.That(hvcC[offset], Is.EqualTo(34));
    var ppsCount = (hvcC[offset + 1] << 8) | hvcC[offset + 2];
    Assert.That(ppsCount, Is.EqualTo(1));
    var ppsLen = (hvcC[offset + 3] << 8) | hvcC[offset + 4];
    Assert.That(ppsLen, Is.EqualTo(TestPps.Length));
  }
}
