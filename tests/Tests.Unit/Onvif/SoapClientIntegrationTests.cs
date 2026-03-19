using System.Net;
using System.Xml.Linq;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Tests.Unit.Onvif;

[TestFixture]
public class SoapClientIntegrationTests
{
  private static readonly XNamespace NsDevice = XmlHelpers.NsDevice;
  private static readonly XNamespace NsMedia = XmlHelpers.NsMedia;
  private static readonly XNamespace NsSchema = XmlHelpers.NsSchema;
  private static readonly XNamespace NsSoap = XmlHelpers.NsSoap;

  [Test]
  public async Task DeviceService_GetDeviceInformation_ParsesResponse()
  {
    var responseXml = BuildSoapResponse(
      new XElement(NsDevice + "GetDeviceInformationResponse",
        new XElement(NsDevice + "Manufacturer", "TestCorp"),
        new XElement(NsDevice + "Model", "TC-400"),
        new XElement(NsDevice + "FirmwareVersion", "2.1.0"),
        new XElement(NsDevice + "SerialNumber", "SN12345"),
        new XElement(NsDevice + "HardwareId", "HW001")));

    using var handler = new MockHandler(responseXml);
    using var http = new HttpClient(handler);
    var soap = new SoapClient(http);
    var device = new DeviceService(soap);
    var creds = new Credentials { Username = "admin", Password = "pass" };

    var info = await device.GetDeviceInformationAsync("http://localhost/onvif/device_service", creds, CancellationToken.None);

    Assert.That(info.Manufacturer, Is.EqualTo("TestCorp"));
    Assert.That(info.Model, Is.EqualTo("TC-400"));
    Assert.That(info.FirmwareVersion, Is.EqualTo("2.1.0"));
    Assert.That(info.SerialNumber, Is.EqualTo("SN12345"));
    Assert.That(info.HardwareId, Is.EqualTo("HW001"));
  }

  [Test]
  public async Task DeviceService_GetCapabilities_ParsesMediaAndEvents()
  {
    var responseXml = BuildSoapResponse(
      new XElement(NsDevice + "GetCapabilitiesResponse",
        new XElement(NsDevice + "Capabilities",
          new XElement(NsSchema + "Media",
            new XElement(NsSchema + "XAddr", "http://localhost/onvif/media")),
          new XElement(NsSchema + "Events",
            new XElement(NsSchema + "XAddr", "http://localhost/onvif/events")),
          new XElement(NsSchema + "PTZ",
            new XElement(NsSchema + "XAddr", "http://localhost/onvif/ptz")))));

    using var handler = new MockHandler(responseXml);
    using var http = new HttpClient(handler);
    var soap = new SoapClient(http);
    var device = new DeviceService(soap);
    var creds = new Credentials { Username = "admin", Password = "pass" };

    var caps = await device.GetCapabilitiesAsync("http://localhost/onvif/device_service", creds, CancellationToken.None);

    Assert.That(caps.MediaUri, Is.EqualTo("http://localhost/onvif/media"));
    Assert.That(caps.EventsUri, Is.EqualTo("http://localhost/onvif/events"));
    Assert.That(caps.PtzUri, Is.EqualTo("http://localhost/onvif/ptz"));
    Assert.That(caps.HasPtz, Is.True);
    Assert.That(caps.HasEvents, Is.True);
  }

  [Test]
  public async Task MediaService_GetProfiles_ParsesProfileList()
  {
    var responseXml = BuildSoapResponse(
      new XElement(NsMedia + "GetProfilesResponse",
        new XElement(NsMedia + "Profiles",
          new XAttribute("token", "MainProfile"),
          new XElement(NsSchema + "Name", "Main Stream"),
          new XElement(NsSchema + "VideoEncoderConfiguration",
            new XElement(NsSchema + "Encoding", "H264"),
            new XElement(NsSchema + "Resolution",
              new XElement(NsSchema + "Width", "1920"),
              new XElement(NsSchema + "Height", "1080")),
            new XElement(NsSchema + "RateControl",
              new XElement(NsSchema + "FrameRateLimit", "30"),
              new XElement(NsSchema + "BitrateLimit", "4096")))),
        new XElement(NsMedia + "Profiles",
          new XAttribute("token", "SubProfile"),
          new XElement(NsSchema + "Name", "Sub Stream"),
          new XElement(NsSchema + "VideoEncoderConfiguration",
            new XElement(NsSchema + "Encoding", "H264"),
            new XElement(NsSchema + "Resolution",
              new XElement(NsSchema + "Width", "640"),
              new XElement(NsSchema + "Height", "480")),
            new XElement(NsSchema + "RateControl",
              new XElement(NsSchema + "FrameRateLimit", "15"),
              new XElement(NsSchema + "BitrateLimit", "512"))))));

    using var handler = new MockHandler(responseXml);
    using var http = new HttpClient(handler);
    var soap = new SoapClient(http);
    var media = new MediaService(soap);
    var creds = new Credentials { Username = "admin", Password = "pass" };

    var profiles = await media.GetProfilesAsync("http://localhost/onvif/media", creds, CancellationToken.None);

    Assert.That(profiles, Has.Count.EqualTo(2));
    Assert.That(profiles[0].Token, Is.EqualTo("MainProfile"));
    Assert.That(profiles[0].Codec, Is.EqualTo("h264"));
    Assert.That(profiles[0].Width, Is.EqualTo(1920));
    Assert.That(profiles[0].Height, Is.EqualTo(1080));
    Assert.That(profiles[0].Fps, Is.EqualTo(30));
    Assert.That(profiles[0].Bitrate, Is.EqualTo(4096));
    Assert.That(profiles[1].Token, Is.EqualTo("SubProfile"));
    Assert.That(profiles[1].Width, Is.EqualTo(640));
  }

  [Test]
  public async Task MediaService_GetStreamUri_ParsesUri()
  {
    var responseXml = BuildSoapResponse(
      new XElement(NsMedia + "GetStreamUriResponse",
        new XElement(NsMedia + "MediaUri",
          new XElement(NsSchema + "Uri", "rtsp://192.168.1.100:554/stream1"))));

    using var handler = new MockHandler(responseXml);
    using var http = new HttpClient(handler);
    var soap = new SoapClient(http);
    var media = new MediaService(soap);
    var creds = new Credentials { Username = "admin", Password = "pass" };

    var uri = await media.GetStreamUriAsync("http://localhost/onvif/media", creds, "MainProfile", CancellationToken.None);

    Assert.That(uri, Is.EqualTo("rtsp://192.168.1.100:554/stream1"));
  }

  [Test]
  public void SoapClient_FaultResponse_ThrowsSoapFaultException()
  {
    var faultXml = $"""
      <?xml version="1.0" encoding="utf-8"?>
      <s:Envelope xmlns:s="{NsSoap}">
        <s:Header/>
        <s:Body>
          <s:Fault>
            <s:Code><s:Value>s:Sender</s:Value></s:Code>
            <s:Reason><s:Text>Not authorized</s:Text></s:Reason>
          </s:Fault>
        </s:Body>
      </s:Envelope>
      """;

    using var handler = new MockHandler(faultXml);
    using var http = new HttpClient(handler);
    var soap = new SoapClient(http);
    var body = new XElement(NsDevice + "GetDeviceInformation");

    var ex = Assert.ThrowsAsync<SoapFaultException>(async () =>
      await soap.SendAsync("http://localhost/test", body, null, CancellationToken.None));
    Assert.That(ex!.Message, Is.EqualTo("Not authorized"));
  }

  private static string BuildSoapResponse(XElement body) =>
    new XDocument(
      new XElement(NsSoap + "Envelope",
        new XAttribute(XNamespace.Xmlns + "s", NsSoap),
        new XElement(NsSoap + "Header"),
        new XElement(NsSoap + "Body", body))).ToString();

  private sealed class MockHandler(string responseXml) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken ct) =>
      Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(responseXml, System.Text.Encoding.UTF8, "application/soap+xml")
      });
  }
}
