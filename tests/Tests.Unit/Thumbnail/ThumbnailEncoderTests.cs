using Analyzer.Thumbnail;

namespace Tests.Unit.Thumbnail;

[TestFixture]
public class ThumbnailEncoderTests
{
  /// <summary>
  /// SCENARIO:
  /// A frame whose luma plane has an odd width and height, which 1080p reaches at the decoder's
  /// output scale
  ///
  /// ACTION:
  /// Encode it
  ///
  /// EXPECTED RESULT:
  /// A JPEG of the source size, since the last luma row and column still have chroma to pair with
  /// </summary>
  [Test]
  public void Encode_OddSizedFrame_CoversEveryLumaSample()
  {
    var frame = Grey(135, 67);

    var thumbnail = new ThumbnailEncoder().Encode(frame, 240, 70);

    Assert.Multiple(() =>
    {
      Assert.That(thumbnail.Width, Is.EqualTo(135));
      Assert.That(thumbnail.Height, Is.EqualTo(67));
      Assert.That(thumbnail.Data, Is.Not.Empty);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A frame larger than the configured bounding box
  ///
  /// ACTION:
  /// Encode it with a bound shorter than its longest edge
  ///
  /// EXPECTED RESULT:
  /// The longest edge meets the bound and the aspect ratio is preserved
  /// </summary>
  [Test]
  public void Encode_OversizedFrame_FitsTheBoundingBox()
  {
    var thumbnail = new ThumbnailEncoder().Encode(Grey(240, 135), 120, 70);

    Assert.Multiple(() =>
    {
      Assert.That(thumbnail.Width, Is.EqualTo(120));
      Assert.That(thumbnail.Height, Is.EqualTo(68));
    });
  }

  private static DecodedFrame Grey(int width, int height)
  {
    var luma = new byte[width * height];
    var chroma = new byte[((width + 1) / 2) * ((height + 1) / 2)];
    Array.Fill(luma, (byte)90);
    Array.Fill(chroma, (byte)128);
    return DecodedFrame.Subsampled(luma, chroma, (byte[])chroma.Clone(), width, height);
  }
}
