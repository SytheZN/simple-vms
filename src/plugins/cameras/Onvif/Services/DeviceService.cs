using System.Xml.Linq;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Cameras.Onvif.Services;

public sealed class DeviceService(SoapClient soap)
{
  public async Task<DeviceInfo> GetDeviceInformationAsync(
    string deviceUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsDevice + "GetDeviceInformation");
    var response = await soap.SendAsync(deviceUri, body, credentials, ct);

    var info = response.Element(XmlHelpers.NsDevice + "GetDeviceInformationResponse");
    return new DeviceInfo
    {
      Manufacturer = info?.Element(XmlHelpers.NsDevice + "Manufacturer")?.Value,
      Model = info?.Element(XmlHelpers.NsDevice + "Model")?.Value,
      FirmwareVersion = info?.Element(XmlHelpers.NsDevice + "FirmwareVersion")?.Value,
      SerialNumber = info?.Element(XmlHelpers.NsDevice + "SerialNumber")?.Value,
      HardwareId = info?.Element(XmlHelpers.NsDevice + "HardwareId")?.Value
    };
  }

  public async Task<DeviceCapabilities> GetCapabilitiesAsync(
    string deviceUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsDevice + "GetCapabilities",
      new XElement(XmlHelpers.NsDevice + "Category", "All"));
    var response = await soap.SendAsync(deviceUri, body, credentials, ct);

    var caps = response
      .Element(XmlHelpers.NsDevice + "GetCapabilitiesResponse")
      ?.Element(XmlHelpers.NsDevice + "Capabilities");

    return new DeviceCapabilities
    {
      MediaUri = caps?.Element(XmlHelpers.NsSchema + "Media")
        ?.Element(XmlHelpers.NsSchema + "XAddr")?.Value,
      EventsUri = caps?.Element(XmlHelpers.NsSchema + "Events")
        ?.Element(XmlHelpers.NsSchema + "XAddr")?.Value,
      PtzUri = caps?.Element(XmlHelpers.NsSchema + "PTZ")
        ?.Element(XmlHelpers.NsSchema + "XAddr")?.Value,
      AnalyticsUri = caps?.Element(XmlHelpers.NsSchema + "Analytics")
        ?.Element(XmlHelpers.NsSchema + "XAddr")?.Value,
      HasPtz = caps?.Element(XmlHelpers.NsSchema + "PTZ") != null,
      HasEvents = caps?.Element(XmlHelpers.NsSchema + "Events") != null,
      HasAudio = HasAudioSupport(caps),
      HasAnalytics = caps?.Element(XmlHelpers.NsSchema + "Analytics") != null
    };
  }

  private static bool HasAudioSupport(XElement? caps)
  {
    var media = caps?.Element(XmlHelpers.NsSchema + "Media");
    if (media == null) return false;
    var streaming = media.Element(XmlHelpers.NsSchema + "StreamingCapabilities");
    return streaming?.Element(XmlHelpers.NsSchema + "RTP_TCP") != null
      || streaming?.Element(XmlHelpers.NsSchema + "RTP_RTSP_TCP") != null;
  }

  public async Task<IReadOnlyDictionary<string, string>> GetServicesAsync(
    string deviceUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsDevice + "GetServices",
      new XElement(XmlHelpers.NsDevice + "IncludeCapability", "false"));
    var response = await soap.SendAsync(deviceUri, body, credentials, ct);

    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var services = response.Element(XmlHelpers.NsDevice + "GetServicesResponse");
    if (services == null) return result;

    foreach (var service in services.Elements(XmlHelpers.NsDevice + "Service"))
    {
      var ns = service.Element(XmlHelpers.NsDevice + "Namespace")?.Value;
      var addr = service.Element(XmlHelpers.NsDevice + "XAddr")?.Value;
      if (!string.IsNullOrEmpty(ns) && !string.IsNullOrEmpty(addr))
        result[ns] = addr;
    }
    return result;
  }
}

public sealed class DeviceInfo
{
  public string? Manufacturer { get; init; }
  public string? Model { get; init; }
  public string? FirmwareVersion { get; init; }
  public string? SerialNumber { get; init; }
  public string? HardwareId { get; init; }
}

public sealed class DeviceCapabilities
{
  public string? MediaUri { get; init; }
  public string? EventsUri { get; init; }
  public string? PtzUri { get; init; }
  public string? AnalyticsUri { get; init; }
  public bool HasPtz { get; init; }
  public bool HasEvents { get; init; }
  public bool HasAudio { get; init; }
  public bool HasAnalytics { get; init; }
}
