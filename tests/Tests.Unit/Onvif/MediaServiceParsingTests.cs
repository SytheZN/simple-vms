using System.Net;
using System.Xml.Linq;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Tests.Unit.Onvif;

[TestFixture]
public class MediaServiceParsingTests
{
  /// <summary>
  /// SCENARIO:
  /// Camera returns GetProfilesResponse with one H.264 profile
  ///
  /// ACTION:
  /// Call GetProfilesAsync
  ///
  /// EXPECTED RESULT:
  /// Returns one profile with codec, resolution, fps, bitrate
  /// </summary>
  [Test]
  public async Task GetProfiles_ParsesH264Profile()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsMedia + "GetProfilesResponse",
      new XElement(XmlHelpers.NsMedia + "Profiles",
        new XAttribute("token", "prof1"),
        new XElement(XmlHelpers.NsSchema + "Name", "Main"),
        new XElement(XmlHelpers.NsSchema + "VideoEncoderConfiguration",
          new XElement(XmlHelpers.NsSchema + "Encoding", "H264"),
          new XElement(XmlHelpers.NsSchema + "Resolution",
            new XElement(XmlHelpers.NsSchema + "Width", "1920"),
            new XElement(XmlHelpers.NsSchema + "Height", "1080")),
          new XElement(XmlHelpers.NsSchema + "RateControl",
            new XElement(XmlHelpers.NsSchema + "FrameRateLimit", "30"),
            new XElement(XmlHelpers.NsSchema + "BitrateLimit", "4096"))))));

    var service = CreateService(responseXml);
    var profiles = await service.GetProfilesAsync(
      "http://cam/media", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(profiles, Has.Count.EqualTo(1));
    Assert.That(profiles[0].Token, Is.EqualTo("prof1"));
    Assert.That(profiles[0].Codec, Is.EqualTo("h264"));
    Assert.That(profiles[0].Width, Is.EqualTo(1920));
    Assert.That(profiles[0].Height, Is.EqualTo(1080));
    Assert.That(profiles[0].Fps, Is.EqualTo(30));
    Assert.That(profiles[0].Bitrate, Is.EqualTo(4096));
  }

  /// <summary>
  /// SCENARIO:
  /// Camera returns GetProfilesResponse with H.265 profile
  ///
  /// ACTION:
  /// Call GetProfilesAsync
  ///
  /// EXPECTED RESULT:
  /// Codec is parsed as "h265"
  /// </summary>
  [Test]
  public async Task GetProfiles_ParsesH265Codec()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsMedia + "GetProfilesResponse",
      new XElement(XmlHelpers.NsMedia + "Profiles",
        new XAttribute("token", "prof1"),
        new XElement(XmlHelpers.NsSchema + "VideoEncoderConfiguration",
          new XElement(XmlHelpers.NsSchema + "Encoding", "H265")))));

    var service = CreateService(responseXml);
    var profiles = await service.GetProfilesAsync(
      "http://cam/media", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(profiles[0].Codec, Is.EqualTo("h265"));
  }

  /// <summary>
  /// SCENARIO:
  /// Camera returns empty GetProfilesResponse
  ///
  /// ACTION:
  /// Call GetProfilesAsync
  ///
  /// EXPECTED RESULT:
  /// Returns empty list
  /// </summary>
  [Test]
  public async Task GetProfiles_Empty_ReturnsEmptyList()
  {
    var responseXml = BuildSoapResponse(
      new XElement(XmlHelpers.NsMedia + "GetProfilesResponse"));

    var service = CreateService(responseXml);
    var profiles = await service.GetProfilesAsync(
      "http://cam/media", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(profiles, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Camera returns GetStreamUriResponse
  ///
  /// ACTION:
  /// Call GetStreamUriAsync
  ///
  /// EXPECTED RESULT:
  /// Returns the RTSP URI
  /// </summary>
  [Test]
  public async Task GetStreamUri_ParsesUri()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsMedia + "GetStreamUriResponse",
      new XElement(XmlHelpers.NsMedia + "MediaUri",
        new XElement(XmlHelpers.NsSchema + "Uri", "rtsp://192.168.1.10:554/stream1"))));

    var service = CreateService(responseXml);
    var uri = await service.GetStreamUriAsync(
      "http://cam/media", Credentials.FromUserPass("admin", ""),
      "prof1", CancellationToken.None);

    Assert.That(uri, Is.EqualTo("rtsp://192.168.1.10:554/stream1"));
  }

  /// <summary>
  /// SCENARIO:
  /// Camera returns GetStreamUriResponse without URI element
  ///
  /// ACTION:
  /// Call GetStreamUriAsync
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public async Task GetStreamUri_MissingUri_ReturnsNull()
  {
    var responseXml = BuildSoapResponse(
      new XElement(XmlHelpers.NsMedia + "GetStreamUriResponse"));

    var service = CreateService(responseXml);
    var uri = await service.GetStreamUriAsync(
      "http://cam/media", Credentials.FromUserPass("admin", ""),
      "prof1", CancellationToken.None);

    Assert.That(uri, Is.Null);
  }

  private static MediaService CreateService(string responseXml)
  {
    var handler = new FakeHttpHandler(responseXml);
    var http = new HttpClient(handler);
    return new MediaService(new SoapClient(http));
  }

  private static string BuildSoapResponse(XElement body)
  {
    return XmlHelpers.BuildEnvelope(body).ToString();
  }

  private sealed class FakeHttpHandler(string responseXml) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken ct)
    {
      return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
      {
        Content = new StringContent(responseXml, System.Text.Encoding.UTF8, "application/soap+xml")
      });
    }
  }
}
