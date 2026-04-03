using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;

namespace Cameras.Onvif.Soap;

public sealed class SoapClient(HttpClient http, ILogger? logger = null)
{
  private const int PaceDelayMs = 50;
  private static readonly ConcurrentDictionary<string, HostLock> HostLocks = new();
  private readonly ILogger _logger = logger ?? NullLogger.Instance;

  public Task<XElement> SendAsync(
    string uri,
    XElement body,
    Credentials? credentials = null,
    CancellationToken ct = default) =>
    SendCoreAsync(uri, body, credentials, paced: true, ct);

  public Task<XElement> SendUnpacedAsync(
    string uri,
    XElement body,
    Credentials? credentials = null,
    CancellationToken ct = default) =>
    SendCoreAsync(uri, body, credentials, paced: false, ct);

  private async Task<XElement> SendCoreAsync(
    string uri,
    XElement body,
    Credentials? credentials,
    bool paced,
    CancellationToken ct)
  {
    var hostKey = GetHostKey(uri);
    var action = body.Name.LocalName;
    SemaphoreSlim? semaphore = null;

    HostLock? hostLock = null;
    if (paced)
    {
      hostLock = HostLocks.GetOrAdd(hostKey, _ => new HostLock());
      Interlocked.Increment(ref hostLock.ActiveCount);
      semaphore = hostLock.Semaphore;
      await semaphore.WaitAsync(ct);
    }

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
    finally
    {
      if (semaphore != null)
        _ = ReleaseAfterDelay(hostKey, hostLock!, semaphore, ct);
    }
  }

  private static async Task ReleaseAfterDelay(
    string hostKey, HostLock hostLock, SemaphoreSlim semaphore, CancellationToken ct)
  {
    try { await Task.Delay(PaceDelayMs, ct); }
    catch (OperationCanceledException) { }
    semaphore.Release();
    if (Interlocked.Decrement(ref hostLock.ActiveCount) <= 0)
      HostLocks.TryRemove(hostKey, out _);
  }

  private static string GetHostKey(string uri) =>
    Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
      ? $"{parsed.Host}:{parsed.Port}"
      : uri;
}

internal sealed class HostLock
{
  public readonly SemaphoreSlim Semaphore = new(1, 1);
  public int ActiveCount;
}

public sealed class SoapFaultException(string message) : Exception(message);
