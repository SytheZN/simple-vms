using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class AvcCBuilderTests
{
  private static readonly byte[] TestSps =
  [
    0x67, 0x42, 0x00, 0x2A, 0x96, 0x35, 0x40, 0xF0,
    0x04, 0x4F, 0xCB, 0x37, 0x01, 0x01, 0x01, 0x40,
    0x00, 0x01, 0xC2, 0x00, 0x00, 0x57, 0xE4, 0x01
  ];

  private static readonly byte[] TestPps = [0x68, 0xCE, 0x38, 0x80];

  /// <summary>
  /// SCENARIO:
  /// Build avcC from SPS and PPS
  ///
  /// ACTION:
  /// Build avcC record
  ///
  /// EXPECTED RESULT:
  /// configurationVersion=1, profile/level from SPS, lengthSizeMinusOne=3,
  /// contains SPS and PPS data
  /// </summary>
  [Test]
  public void Build_HasCorrectHeaderAndContainsSpsAndPps()
  {
    var spsInfo = H264SpsParser.Parse(TestSps);
    var avcC = AvcCBuilder.Build(TestSps, TestPps, spsInfo);

    Assert.That(avcC[0], Is.EqualTo(1));
    Assert.That(avcC[1], Is.EqualTo(spsInfo.ProfileIdc));
    Assert.That(avcC[2], Is.EqualTo(spsInfo.ProfileCompatibility));
    Assert.That(avcC[3], Is.EqualTo(spsInfo.LevelIdc));
    Assert.That(avcC[4] & 0x03, Is.EqualTo(3));
    Assert.That(avcC[5] & 0x1F, Is.EqualTo(1));

    var spsLen = (avcC[6] << 8) | avcC[7];
    Assert.That(spsLen, Is.EqualTo(TestSps.Length));
    Assert.That(avcC.AsSpan(8, spsLen).ToArray(), Is.EqualTo(TestSps));

    var ppsCountOffset = 8 + spsLen;
    Assert.That(avcC[ppsCountOffset], Is.EqualTo(1));
    var ppsLen = (avcC[ppsCountOffset + 1] << 8) | avcC[ppsCountOffset + 2];
    Assert.That(ppsLen, Is.EqualTo(TestPps.Length));
    Assert.That(avcC.AsSpan(ppsCountOffset + 3, ppsLen).ToArray(), Is.EqualTo(TestPps));
  }
}
