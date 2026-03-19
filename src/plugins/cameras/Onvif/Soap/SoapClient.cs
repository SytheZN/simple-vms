using System.Net.Http.Headers;
using System.Xml.Linq;
using Shared.Models;

namespace Cameras.Onvif.Soap;

public sealed class SoapClient(HttpClient http)
{
  public async Task<XElement> SendAsync(
    string uri,
    XElement body,
    Credentials? credentials = null,
    CancellationToken ct = default)
  {
    var security = credentials != null
      ? WsUsernameToken.Build(credentials.Username, credentials.Password)
      : null;
    var envelope = XmlHelpers.BuildEnvelope(body, security);

    using var content = new StringContent(envelope.ToString());
    content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml") { CharSet = "utf-8" };

    using var response = await http.PostAsync(uri, content, ct);
    var responseText = await response.Content.ReadAsStringAsync(ct);
    var doc = XDocument.Parse(responseText);

    var fault = XmlHelpers.GetFault(doc);
    if (fault != null)
    {
      var reason = fault.Element(XmlHelpers.NsSoap + "Reason")
        ?.Element(XmlHelpers.NsSoap + "Text")?.Value ?? "Unknown SOAP fault";
      throw new SoapFaultException(reason);
    }

    return XmlHelpers.GetBody(doc)
      ?? throw new SoapFaultException("Empty SOAP response body");
  }
}

public sealed class SoapFaultException(string message) : Exception(message);
