using System.Net;
using System.Xml.Linq;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Tests.Unit.Onvif;

[TestFixture]
public class DeviceServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// Camera returns GetDeviceInformationResponse with all fields
  ///
  /// ACTION:
  /// Call GetDeviceInformationAsync
  ///
  /// EXPECTED RESULT:
  /// DeviceInfo populated with manufacturer, model, firmware, serial, hardware
  /// </summary>
  [Test]
  public async Task GetDeviceInformation_ParsesAllFields()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsDevice + "GetDeviceInformationResponse",
      new XElement(XmlHelpers.NsDevice + "Manufacturer", "Acme"),
      new XElement(XmlHelpers.NsDevice + "Model", "Cam2000"),
      new XElement(XmlHelpers.NsDevice + "FirmwareVersion", "1.2.3"),
      new XElement(XmlHelpers.NsDevice + "SerialNumber", "SN123"),
      new XElement(XmlHelpers.NsDevice + "HardwareId", "HW456")));

    var service = CreateService(responseXml);
    var info = await service.GetDeviceInformationAsync(
      "http://cam/device_service", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(info.Manufacturer, Is.EqualTo("Acme"));
    Assert.That(info.Model, Is.EqualTo("Cam2000"));
    Assert.That(info.FirmwareVersion, Is.EqualTo("1.2.3"));
    Assert.That(info.SerialNumber, Is.EqualTo("SN123"));
    Assert.That(info.HardwareId, Is.EqualTo("HW456"));
  }

  /// <summary>
  /// SCENARIO:
  /// Camera returns capabilities with media, events, and PTZ URIs
  ///
  /// ACTION:
  /// Call GetCapabilitiesAsync
  ///
  /// EXPECTED RESULT:
  /// DeviceCapabilities has all URIs and capability flags
  /// </summary>
  [Test]
  public async Task GetCapabilities_ParsesUrisAndFlags()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsDevice + "GetCapabilitiesResponse",
      new XElement(XmlHelpers.NsDevice + "Capabilities",
        new XElement(XmlHelpers.NsSchema + "Media",
          new XElement(XmlHelpers.NsSchema + "XAddr", "http://cam/media")),
        new XElement(XmlHelpers.NsSchema + "Events",
          new XElement(XmlHelpers.NsSchema + "XAddr", "http://cam/events")),
        new XElement(XmlHelpers.NsSchema + "PTZ",
          new XElement(XmlHelpers.NsSchema + "XAddr", "http://cam/ptz")))));

    var service = CreateService(responseXml);
    var caps = await service.GetCapabilitiesAsync(
      "http://cam/device_service", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(caps.MediaUri, Is.EqualTo("http://cam/media"));
    Assert.That(caps.EventsUri, Is.EqualTo("http://cam/events"));
    Assert.That(caps.PtzUri, Is.EqualTo("http://cam/ptz"));
    Assert.That(caps.HasPtz, Is.True);
    Assert.That(caps.HasEvents, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Camera returns capabilities without PTZ
  ///
  /// ACTION:
  /// Call GetCapabilitiesAsync
  ///
  /// EXPECTED RESULT:
  /// HasPtz is false, PtzUri is null
  /// </summary>
  [Test]
  public async Task GetCapabilities_NoPtz_FlagFalse()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsDevice + "GetCapabilitiesResponse",
      new XElement(XmlHelpers.NsDevice + "Capabilities",
        new XElement(XmlHelpers.NsSchema + "Media",
          new XElement(XmlHelpers.NsSchema + "XAddr", "http://cam/media")))));

    var service = CreateService(responseXml);
    var caps = await service.GetCapabilitiesAsync(
      "http://cam/device_service", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(caps.HasPtz, Is.False);
    Assert.That(caps.PtzUri, Is.Null);
    Assert.That(caps.HasEvents, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Camera returns capabilities with an Analytics section
  ///
  /// ACTION:
  /// Call GetCapabilitiesAsync
  ///
  /// EXPECTED RESULT:
  /// HasAnalytics is true and AnalyticsUri is populated
  /// </summary>
  [Test]
  public async Task GetCapabilities_WithAnalytics_FlagTrueAndUriSet()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsDevice + "GetCapabilitiesResponse",
      new XElement(XmlHelpers.NsDevice + "Capabilities",
        new XElement(XmlHelpers.NsSchema + "Media",
          new XElement(XmlHelpers.NsSchema + "XAddr", "http://cam/media")),
        new XElement(XmlHelpers.NsSchema + "Analytics",
          new XElement(XmlHelpers.NsSchema + "XAddr", "http://cam/analytics")))));

    var service = CreateService(responseXml);
    var caps = await service.GetCapabilitiesAsync(
      "http://cam/device_service", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(caps.HasAnalytics, Is.True);
    Assert.That(caps.AnalyticsUri, Is.EqualTo("http://cam/analytics"));
  }

  /// <summary>
  /// SCENARIO:
  /// Camera returns capabilities without Analytics section
  ///
  /// ACTION:
  /// Call GetCapabilitiesAsync
  ///
  /// EXPECTED RESULT:
  /// HasAnalytics is false and AnalyticsUri is null
  /// </summary>
  [Test]
  public async Task GetCapabilities_NoAnalytics_FlagFalseAndUriNull()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsDevice + "GetCapabilitiesResponse",
      new XElement(XmlHelpers.NsDevice + "Capabilities",
        new XElement(XmlHelpers.NsSchema + "Media",
          new XElement(XmlHelpers.NsSchema + "XAddr", "http://cam/media")))));

    var service = CreateService(responseXml);
    var caps = await service.GetCapabilitiesAsync(
      "http://cam/device_service", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(caps.HasAnalytics, Is.False);
    Assert.That(caps.AnalyticsUri, Is.Null);
  }

  private static DeviceService CreateService(string responseXml)
  {
    var handler = new FakeHttpHandler(responseXml);
    var http = new HttpClient(handler);
    return new DeviceService(new SoapClient(http));
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
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(responseXml, System.Text.Encoding.UTF8, "application/soap+xml")
      });
    }
  }
}
