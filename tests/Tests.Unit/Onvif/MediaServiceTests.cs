using Cameras.Onvif.Services;
using Shared.Models;

namespace Tests.Unit.Onvif;

[TestFixture]
public class MediaServiceTests
{
  [Test]
  public void ToStreamProfile_FirstProfile_MapsToMain()
  {
    var profile = new OnvifProfile
    {
      Token = "prof1",
      Name = "Main Stream",
      Codec = "h264",
      Width = 1920,
      Height = 1080,
      Fps = 30,
      Bitrate = 4096
    };

    var result = MediaService.ToStreamProfile(profile, "rtsp://192.168.1.100/stream1", 0);

    Assert.That(result.Profile, Is.EqualTo("main"));
    Assert.That(result.Kind, Is.EqualTo(StreamKind.Quality));
    Assert.That(result.FormatId, Is.EqualTo("fmp4"));
    Assert.That(result.Codec, Is.EqualTo("h264"));
    Assert.That(result.Resolution, Is.EqualTo("1920x1080"));
    Assert.That(result.Fps, Is.EqualTo(30));
    Assert.That(result.Bitrate, Is.EqualTo(4096));
    Assert.That(result.Uri, Is.EqualTo("rtsp://192.168.1.100/stream1"));
  }

  [Test]
  public void ToStreamProfile_SecondProfile_MapsToSub()
  {
    var profile = new OnvifProfile
    {
      Token = "prof2",
      Codec = "h264",
      Width = 640,
      Height = 480,
      Fps = 15,
      Bitrate = 512
    };

    var result = MediaService.ToStreamProfile(profile, "rtsp://192.168.1.100/stream2", 1);

    Assert.That(result.Profile, Is.EqualTo("sub"));
    Assert.That(result.Resolution, Is.EqualTo("640x480"));
  }

  [Test]
  public void ToStreamProfile_ThirdProfile_MapsToStream2()
  {
    var profile = new OnvifProfile
    {
      Token = "prof3",
      Codec = "h265",
      Width = 1280,
      Height = 720
    };

    var result = MediaService.ToStreamProfile(profile, "rtsp://192.168.1.100/stream3", 2);

    Assert.That(result.Profile, Is.EqualTo("stream2"));
  }

  [Test]
  public void ToStreamProfile_NullResolution_OmitsResolution()
  {
    var profile = new OnvifProfile
    {
      Token = "prof1",
      Codec = "h264"
    };

    var result = MediaService.ToStreamProfile(profile, "rtsp://host/s", 0);

    Assert.That(result.Resolution, Is.Null);
  }

  [Test]
  public void ToStreamProfile_PartialResolution_OmitsResolution()
  {
    var profile = new OnvifProfile
    {
      Token = "prof1",
      Codec = "h264",
      Width = 1920
    };

    var result = MediaService.ToStreamProfile(profile, "rtsp://host/s", 0);

    Assert.That(result.Resolution, Is.Null);
  }
}
