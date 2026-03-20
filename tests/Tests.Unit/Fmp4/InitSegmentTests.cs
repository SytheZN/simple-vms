using System.Buffers.Binary;
using System.Text;
using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class InitSegmentTests
{
  // Minimal valid H.264 Baseline SPS: 1920x1080
  private static readonly byte[] TestSps =
  [
    0x67, 0x42, 0x00, 0x2A, 0x96, 0x35, 0x40, 0xF0,
    0x04, 0x4F, 0xCB, 0x37, 0x01, 0x01, 0x01, 0x40,
    0x00, 0x01, 0xC2, 0x00, 0x00, 0x57, 0xE4, 0x01
  ];

  private static readonly byte[] TestPps = [0x68, 0xCE, 0x38, 0x80];

  /// <summary>
  /// SCENARIO:
  /// Generate ftyp box
  ///
  /// ACTION:
  /// Call FtypBuilder.Build()
  ///
  /// EXPECTED RESULT:
  /// Box starts with size + "ftyp", major brand is "isom", has compatible brands
  /// </summary>
  [Test]
  public void Ftyp_HasCorrectBrands()
  {
    var ftyp = FtypBuilder.Build();
    var type = Encoding.ASCII.GetString(ftyp, 4, 4);
    var majorBrand = Encoding.ASCII.GetString(ftyp, 8, 4);
    var minorVersion = BinaryPrimitives.ReadUInt32BigEndian(ftyp.AsSpan(12));

    Assert.That(type, Is.EqualTo("ftyp"));
    Assert.That(majorBrand, Is.EqualTo("isom"));
    Assert.That(minorVersion, Is.EqualTo(512u));
  }

  /// <summary>
  /// SCENARIO:
  /// Generate H.264 init segment (ftyp + moov)
  ///
  /// ACTION:
  /// Build init segment from test SPS/PPS
  ///
  /// EXPECTED RESULT:
  /// Starts with ftyp box, followed by moov box
  /// </summary>
  [Test]
  public void H264Init_ContainsFtypAndMoov()
  {
    var spsInfo = H264SpsParser.Parse(TestSps);
    var avcC = AvcCBuilder.Build(TestSps, TestPps, spsInfo);
    var ftyp = FtypBuilder.Build();
    var moov = MoovBuilder.BuildH264(spsInfo.Width, spsInfo.Height, 90000, avcC);

    var init = new byte[ftyp.Length + moov.Length];
    ftyp.CopyTo(init, 0);
    moov.CopyTo(init, ftyp.Length);

    var ftypType = Encoding.ASCII.GetString(init, 4, 4);
    var ftypSize = BinaryPrimitives.ReadUInt32BigEndian(init);
    var moovType = Encoding.ASCII.GetString(init, (int)ftypSize + 4, 4);

    Assert.That(ftypType, Is.EqualTo("ftyp"));
    Assert.That(moovType, Is.EqualTo("moov"));
  }

  /// <summary>
  /// SCENARIO:
  /// moov box contains avcC with the SPS/PPS bytes
  ///
  /// ACTION:
  /// Build H.264 moov, search for avcC box, verify it contains the SPS
  ///
  /// EXPECTED RESULT:
  /// avcC box found inside moov, contains the original SPS bytes
  /// </summary>
  [Test]
  public void H264Moov_ContainsAvcCWithSps()
  {
    var spsInfo = H264SpsParser.Parse(TestSps);
    var avcC = AvcCBuilder.Build(TestSps, TestPps, spsInfo);
    var moov = MoovBuilder.BuildH264(spsInfo.Width, spsInfo.Height, 90000, avcC);

    var avcCOffset = FindBox(moov, "avcC");
    Assert.That(avcCOffset, Is.GreaterThan(-1), "avcC box not found in moov");

    var dataStart = avcCOffset + 8;
    Assert.That(moov[dataStart], Is.EqualTo(1), "configurationVersion");
    Assert.That(moov[dataStart + 1], Is.EqualTo(spsInfo.ProfileIdc));
    Assert.That(moov[dataStart + 3], Is.EqualTo(spsInfo.LevelIdc));
  }

  /// <summary>
  /// SCENARIO:
  /// moov contains correct video dimensions in tkhd
  ///
  /// ACTION:
  /// Build H.264 moov for 1920x1080
  ///
  /// EXPECTED RESULT:
  /// tkhd box contains 1920x1080 as 16.16 fixed-point values
  /// </summary>
  [Test]
  public void H264Moov_TkhdHasCorrectDimensions()
  {
    var spsInfo = H264SpsParser.Parse(TestSps);
    var avcC = AvcCBuilder.Build(TestSps, TestPps, spsInfo);
    var moov = MoovBuilder.BuildH264(spsInfo.Width, spsInfo.Height, 90000, avcC);

    var tkhdOffset = FindBox(moov, "tkhd");
    Assert.That(tkhdOffset, Is.GreaterThan(-1), "tkhd not found");

    var tkhdSize = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(tkhdOffset));
    var widthOffset = tkhdOffset + (int)tkhdSize - 8;
    var heightOffset = tkhdOffset + (int)tkhdSize - 4;

    var width = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(widthOffset)) >> 16;
    var height = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(heightOffset)) >> 16;

    Assert.That(width, Is.EqualTo(1920u));
    Assert.That(height, Is.EqualTo(1080u));
  }

  /// <summary>
  /// SCENARIO:
  /// moov contains mvex > trex for fragmented MP4
  ///
  /// ACTION:
  /// Build H.264 moov, find trex box
  ///
  /// EXPECTED RESULT:
  /// trex box exists with track_id=1
  /// </summary>
  [Test]
  public void H264Moov_ContainsMvexTrex()
  {
    var spsInfo = H264SpsParser.Parse(TestSps);
    var avcC = AvcCBuilder.Build(TestSps, TestPps, spsInfo);
    var moov = MoovBuilder.BuildH264(spsInfo.Width, spsInfo.Height, 90000, avcC);

    var trexOffset = FindBox(moov, "trex");
    Assert.That(trexOffset, Is.GreaterThan(-1), "trex not found");

    var trackId = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(trexOffset + 12));
    Assert.That(trackId, Is.EqualTo(1u));
  }

  /// <summary>
  /// SCENARIO:
  /// moov stts/stsc/stsz/stco have zero entries (fragmented MP4)
  ///
  /// ACTION:
  /// Build H.264 moov, find stts, check entry count
  ///
  /// EXPECTED RESULT:
  /// stts entry_count is 0
  /// </summary>
  [Test]
  public void H264Moov_SampleTablesAreEmpty()
  {
    var spsInfo = H264SpsParser.Parse(TestSps);
    var avcC = AvcCBuilder.Build(TestSps, TestPps, spsInfo);
    var moov = MoovBuilder.BuildH264(spsInfo.Width, spsInfo.Height, 90000, avcC);

    var sttsOffset = FindBox(moov, "stts");
    Assert.That(sttsOffset, Is.GreaterThan(-1), "stts not found");

    var entryCount = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(sttsOffset + 12));
    Assert.That(entryCount, Is.EqualTo(0u));
  }

  private static int FindBox(byte[] data, string type)
  {
    var target = Encoding.ASCII.GetBytes(type);
    for (var i = 0; i <= data.Length - 8; i++)
    {
      if (data[i + 4] == target[0] && data[i + 5] == target[1]
        && data[i + 6] == target[2] && data[i + 7] == target[3])
        return i;
    }
    return -1;
  }
}
