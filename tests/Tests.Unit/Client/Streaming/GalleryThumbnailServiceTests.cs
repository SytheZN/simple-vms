using System.Buffers.Binary;
using Client.Core.Streaming;
using Shared.Api;
using Shared.Models;

namespace Tests.Unit.Client.Streaming;

[TestFixture]
public class GalleryThumbnailServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// A camera declares a thumbnail stream alongside quality profiles and other metadata streams
  ///
  /// ACTION:
  /// Select the thumbnail profiles
  ///
  /// EXPECTED RESULT:
  /// Only the mjpeg thumbnail profile is chosen
  /// </summary>
  [Test]
  public void ThumbnailProfiles_SelectsOnlyMjpegThumbnails()
  {
    var camera = MakeCamera(
      MakeStream("main", StreamKind.Quality, "fmp4"),
      MakeStream("motion-grid-main", StreamKind.Metadata, "motion-grid"),
      MakeStream("main-thumbnail", StreamKind.Metadata, "mjpeg"));

    Assert.That(GalleryThumbnailService.ThumbnailProfiles(camera),
      Is.EqualTo(new[] { "main-thumbnail" }));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera declares no thumbnail stream
  ///
  /// ACTION:
  /// Select the thumbnail profiles
  ///
  /// EXPECTED RESULT:
  /// Nothing is chosen, so no subscription is held against that camera
  /// </summary>
  [Test]
  public void ThumbnailProfiles_NoThumbnailStream_SelectsNothing()
  {
    var camera = MakeCamera(MakeStream("main", StreamKind.Quality, "fmp4"));

    Assert.That(GalleryThumbnailService.ThumbnailProfiles(camera), Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A muxed thumbnail fragment arrives with the JPEG behind its framing header
  ///
  /// ACTION:
  /// Unwrap the fragment
  ///
  /// EXPECTED RESULT:
  /// The JPEG payload is returned without the header
  /// </summary>
  [Test]
  public void Unwrap_ValidFragment_ReturnsPayload()
  {
    byte[] payload = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02];

    var unwrapped = GalleryThumbnailService.Unwrap(MakeFragment(payload));

    Assert.That(unwrapped.ToArray(), Is.EqualTo(payload));
  }

  /// <summary>
  /// SCENARIO:
  /// A fragment arrives that is not the expected framing
  ///
  /// ACTION:
  /// Unwrap a fragment with the wrong magic, and one truncated below the header
  ///
  /// EXPECTED RESULT:
  /// Both are rejected rather than decoded as an image
  /// </summary>
  [Test]
  public void Unwrap_MalformedFragment_ReturnsEmpty()
  {
    var wrongMagic = MakeFragment([0xFF, 0xD8]);
    wrongMagic[0] = (byte)'X';

    Assert.Multiple(() =>
    {
      Assert.That(GalleryThumbnailService.Unwrap(wrongMagic).IsEmpty, Is.True);
      Assert.That(GalleryThumbnailService.Unwrap(new byte[8]).IsEmpty, Is.True);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A fragment claims a payload longer than the bytes actually present
  ///
  /// ACTION:
  /// Unwrap the fragment
  ///
  /// EXPECTED RESULT:
  /// It is rejected rather than read past the end of the buffer
  /// </summary>
  [Test]
  public void Unwrap_LengthBeyondBuffer_ReturnsEmpty()
  {
    var fragment = MakeFragment([0xFF, 0xD8]);
    BinaryPrimitives.WriteUInt32LittleEndian(fragment.AsSpan(13), 4096);

    Assert.That(GalleryThumbnailService.Unwrap(fragment).IsEmpty, Is.True);
  }

  private static byte[] MakeFragment(byte[] payload)
  {
    var fragment = new byte[17 + payload.Length];
    "MJPG"u8.CopyTo(fragment);
    fragment[4] = 1;
    BinaryPrimitives.WriteUInt64LittleEndian(fragment.AsSpan(5), 1_000_000);
    BinaryPrimitives.WriteUInt32LittleEndian(fragment.AsSpan(13), (uint)payload.Length);
    payload.CopyTo(fragment, 17);
    return fragment;
  }

  private static CameraDto MakeCamera(params StreamProfileDto[] streams) => new()
  {
    Id = Guid.NewGuid(),
    Name = "Cam",
    Address = "192.168.1.1",
    Status = "online",
    ProviderId = "onvif",
    Streams = streams,
    Capabilities = []
  };

  private static StreamProfileDto MakeStream(string profile, StreamKind kind, string formatId) => new()
  {
    Profile = profile,
    Kind = kind,
    FormatId = formatId,
    Codec = "",
    Resolution = "",
    Fps = 0,
    RecordingEnabled = false
  };
}
