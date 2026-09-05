using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;

namespace Cameras.Onvif.Soap;

public sealed class SoapClient(HttpClient http, ILogger? logger = null) : ISoapClient
{
  private readonly ILogger _logger = logger ?? NullLogger.Instance;

  public async Task<XElement> SendAsync(
    string uri,
    XElement body,
    Credentials? credentials = null,
    CancellationToken ct = default,
    bool logFaults = true)
  {
    var hostKey = GetHostKey(uri);
    var action = body.Name.LocalName;

    _logger.LogDebug("SOAP {Host} >> {Action}", hostKey, action);
    try
    {
      var security = credentials != null
        ? WsUsernameToken.Build(credentials.Get("username") ?? "", credentials.Get("password") ?? "")
        : null;
      var envelope = XmlHelpers.BuildEnvelope(body, security, uri);

      using var content = new StringContent(envelope.ToString());
      content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml") { CharSet = "utf-8" };

      using var response = await http.PostAsync(uri, content, ct);
      var responseText = await response.Content.ReadAsStringAsync(ct);
      _logger.LogDebug("SOAP {Host} << {Action} ({Status})", hostKey, action, (int)response.StatusCode);
      _logger.LogTrace("SOAP {Host} {Action} response:\n{Body}", hostKey, action, responseText);
      var doc = XDocument.Parse(responseText);

      var fault = XmlHelpers.GetFault(doc);
      if (fault != null)
      {
        var reason = fault.Element(XmlHelpers.NsSoap + "Reason")
          ?.Element(XmlHelpers.NsSoap + "Text")?.Value ?? "Unknown SOAP fault";
        if (logFaults)
          _logger.LogWarning("SOAP {Host} {Action} FAULT: {Reason}", hostKey, action, reason);
        throw new SoapFaultException(reason);
      }

      return XmlHelpers.GetBody(doc)
        ?? throw new SoapFaultException("Empty SOAP response body");
    }
    catch (Exception ex) when (ex is not SoapFaultException and not OperationCanceledException)
    {
      _logger.LogWarning("SOAP {Host} {Action} ERROR: {Message}", hostKey, action, ex.Message);
      throw;
    }
  }

  internal static string GetHostKey(string uri) =>
    Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
      ? $"{parsed.Host}:{parsed.Port}"
      : uri;
}

public sealed class SoapFaultException(string message) : Exception(message);
