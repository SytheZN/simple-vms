using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Server.Core.PortForwarding;

public sealed class UpnpClient
{
  private static readonly XNamespace SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";
  private static readonly XNamespace ControlNs = "urn:schemas-upnp-org:control-1-0";
  private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

  private readonly HttpClient _http;
  private readonly Uri _controlUrl;
  private readonly string _serviceType;

  public UpnpClient(HttpClient http, Uri controlUrl, string serviceType)
  {
    _http = http;
    _controlUrl = controlUrl;
    _serviceType = serviceType;
  }

  public async Task<string> GetExternalIPAsync(CancellationToken ct)
  {
    var result = await InvokeAsync("GetExternalIPAddress", [], ct);
    return result.Descendants("NewExternalIPAddress").FirstOrDefault()?.Value
      ?? throw new UpnpSoapFaultException(null, "Missing NewExternalIPAddress in response", result.ToString());
  }

  public Task AddPortMappingAsync(
    ushort externalPort, ushort internalPort, string internalClient,
    uint leaseSeconds, string description, CancellationToken ct) =>
    InvokeAsync("AddPortMapping",
      [
        ("NewRemoteHost", ""),
        ("NewExternalPort", externalPort.ToString()),
        ("NewProtocol", "TCP"),
        ("NewInternalPort", internalPort.ToString()),
        ("NewInternalClient", internalClient),
        ("NewEnabled", "1"),
        ("NewPortMappingDescription", description),
        ("NewLeaseDuration", leaseSeconds.ToString())
      ], ct);

  public Task DeletePortMappingAsync(ushort externalPort, CancellationToken ct) =>
    InvokeAsync("DeletePortMapping",
      [
        ("NewRemoteHost", ""),
        ("NewExternalPort", externalPort.ToString()),
        ("NewProtocol", "TCP")
      ], ct);

  private async Task<XElement> InvokeAsync(
    string action, (string Name, string Value)[] args, CancellationToken ct)
  {
    var body = BuildEnvelope(action, args);
    using var request = new HttpRequestMessage(HttpMethod.Post, _controlUrl)
    {
      Content = new StringContent(body, Encoding.UTF8, "text/xml")
    };
    request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
    request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{_serviceType}#{action}\"");

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(CallTimeout);
    var response = await _http.SendAsync(request, timeout.Token);
    var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);

    var doc = XDocument.Parse(responseBody);
    var fault = doc.Descendants(SoapNs + "Fault").FirstOrDefault();
    if (fault != null || !response.IsSuccessStatusCode)
    {
      var errorCode = doc.Descendants(ControlNs + "errorCode").FirstOrDefault()?.Value
        ?? doc.Descendants("errorCode").FirstOrDefault()?.Value;
      var errorDescription = doc.Descendants(ControlNs + "errorDescription").FirstOrDefault()?.Value
        ?? doc.Descendants("errorDescription").FirstOrDefault()?.Value;
      int.TryParse(errorCode, out var code);
      throw new UpnpSoapFaultException(
        errorCode != null ? code : null,
        errorDescription,
        responseBody);
    }

    return doc.Root ?? throw new UpnpSoapFaultException(null, "Empty response", responseBody);
  }

  private string BuildEnvelope(string action, (string Name, string Value)[] args)
  {
    var sb = new StringBuilder();
    sb.Append("""<?xml version="1.0"?>""");
    sb.Append("""<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">""");
    sb.Append("<s:Body>");
    sb.Append($"<u:{action} xmlns:u=\"{System.Security.SecurityElement.Escape(_serviceType)}\">");
    foreach (var (name, value) in args)
    {
      sb.Append('<').Append(name).Append('>');
      sb.Append(System.Security.SecurityElement.Escape(value));
      sb.Append("</").Append(name).Append('>');
    }
    sb.Append($"</u:{action}>");
    sb.Append("</s:Body></s:Envelope>");
    return sb.ToString();
  }
}
