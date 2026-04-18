using Client.Core.Decoding;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class CodecConfigTests
{
  /// <summary>
  /// SCENARIO:
  /// CodecParameters is constructed with a codec, dimensions, and extradata
  ///
  /// ACTION:
  /// Read every property after construction
  ///
  /// EXPECTED RESULT:
  /// Each property reflects the constructor argument
  /// </summary>
  [Test]
  public void CodecParameters_PropertiesRoundTrip()
  {
    var extradata = new byte[] { 0x01, 0x42, 0xC0, 0x1E };

    var p = new CodecParameters(VideoCodec.H264, 1920, 1080, extradata);

    Assert.Multiple(() =>
    {
      Assert.That(p.Codec, Is.EqualTo(VideoCodec.H264));
      Assert.That(p.Width, Is.EqualTo(1920));
      Assert.That(p.Height, Is.EqualTo(1080));
      Assert.That(p.Extradata, Is.SameAs(extradata));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Two CodecParameters records hold equivalent values
  ///
  /// ACTION:
  /// Compare via record value equality
  ///
  /// EXPECTED RESULT:
  /// Records compare equal when constructed with the same fields, including the
  /// same extradata reference (record equality on byte[] is reference-based)
  /// </summary>
  [Test]
  public void CodecParameters_RecordEquality()
  {
    var data = new byte[] { 1, 2, 3 };

    var a = new CodecParameters(VideoCodec.H265, 640, 480, data);
    var b = new CodecParameters(VideoCodec.H265, 640, 480, data);

    Assert.That(a, Is.EqualTo(b));
  }

  /// <summary>
  /// SCENARIO:
  /// DemuxedSample carries timing, payload and key-frame flag
  ///
  /// ACTION:
  /// Construct a sample and read every field
  ///
  /// EXPECTED RESULT:
  /// All fields round-trip; IsKey is honoured
  /// </summary>
  [Test]
  public void DemuxedSample_PropertiesRoundTrip()
  {
    var data = new byte[] { 0xAA, 0xBB }.AsMemory();

    var s = new DemuxedSample(data, 1_000_000, 990_000, 33_333, IsKey: true);

    Assert.Multiple(() =>
    {
      Assert.That(s.Data.Length, Is.EqualTo(2));
      Assert.That(s.TimestampUs, Is.EqualTo(1_000_000));
      Assert.That(s.DecodeTimestampUs, Is.EqualTo(990_000));
      Assert.That(s.DurationUs, Is.EqualTo(33_333));
      Assert.That(s.IsKey, Is.True);
    });
  }
}
