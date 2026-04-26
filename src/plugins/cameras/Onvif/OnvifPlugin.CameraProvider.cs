using System.Net;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Cameras.Onvif;

public sealed partial class OnvifProvider : ICameraProvider
{
  public string ProviderId => "onvif";

  public async Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(
    DiscoveryOptions options, CancellationToken ct)
  {
    var credentials = ResolveCredentials(options);
    var allAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var multicastAddresses = await WsDiscovery.ProbeAsync(ct);
    foreach (var addr in multicastAddresses)
      allAddresses.Add(addr);

    if (options.Subnets is { Length: > 0 })
    {
      var scannedAddresses = await WsDiscovery.ScanSubnetsAsync(
        _http, options.Subnets, options.Ports, ct);
      foreach (var addr in scannedAddresses)
        allAddresses.Add(addr);
    }

    var cameras = new List<DiscoveredCamera>();
    foreach (var address in allAddresses)
    {
      var hostname = ReverseLookup(address);
      string? name = null, manufacturer = null, model = null;

      try
      {
        var info = await _device.GetDeviceInformationAsync(address, credentials, ct);
        manufacturer = info.Manufacturer;
        model = info.Model;
        name = model != null ? $"{manufacturer} {model}" : manufacturer;
      }
      catch
      {
      }

      cameras.Add(new DiscoveredCamera
      {
        Address = address,
        Hostname = hostname,
        Name = name,
        Manufacturer = manufacturer,
        Model = model,
        ProviderId = ProviderId
      });
    }

    return cameras;
  }

  public async Task<CameraConfiguration> ConfigureAsync(
    string address, Credentials credentials, CancellationToken ct)
  {
    var info = await _device.GetDeviceInformationAsync(address, credentials, ct);
    var caps = await _device.GetCapabilitiesAsync(address, credentials, ct);

    var media2Uri = await TryGetMedia2UriAsync(address, credentials, ct);
    var mediaUri = RewriteServiceUri(
      caps.MediaUri ?? address.Replace("/device_service", "/media_service"), address);

    var (streams, hasRealPtz) = await ProbeProfilesAsync(media2Uri, mediaUri, address, credentials, ct);

    var capabilities = new List<string>();
    if (hasRealPtz) capabilities.Add("ptz");
    if (caps.HasAudio) capabilities.Add("audio");
    if (caps.HasEvents) capabilities.Add("events");
    if (caps.HasAnalytics) capabilities.Add("analytics");

    var config = new Dictionary<string, string>
    {
      ["deviceUri"] = address,
      ["manufacturer"] = info.Manufacturer ?? "",
      ["model"] = info.Model ?? "",
      ["serialNumber"] = info.SerialNumber ?? "",
      ["firmwareVersion"] = info.FirmwareVersion ?? ""
    };
    if (caps.MediaUri != null) config["mediaUri"] = RewriteServiceUri(caps.MediaUri, address);
    if (media2Uri != null) config["media2Uri"] = media2Uri;
    if (caps.EventsUri != null) config["eventsUri"] = RewriteServiceUri(caps.EventsUri, address);
    if (caps.PtzUri != null) config["ptzUri"] = RewriteServiceUri(caps.PtzUri, address);
    if (caps.AnalyticsUri != null) config["analyticsUri"] = RewriteServiceUri(caps.AnalyticsUri, address);

    return new CameraConfiguration
    {
      Address = address,
      Name = info.Model != null
        ? $"{info.Manufacturer} {info.Model}" : info.Manufacturer ?? "ONVIF Camera",
      Streams = streams,
      Capabilities = [.. capabilities],
      Config = config
    };
  }

  private async Task<string?> TryGetMedia2UriAsync(
    string address, Credentials credentials, CancellationToken ct)
  {
    try
    {
      var services = await _device.GetServicesAsync(address, credentials, ct);
      if (services.TryGetValue(
        "http://www.onvif.org/ver20/media/wsdl", out var uri))
        return RewriteServiceUri(uri, address);
    }
    catch (Exception ex) when (
      ex is HttpRequestException or InvalidOperationException or System.Xml.XmlException
      or SoapFaultException or TaskCanceledException or IOException)
    {
      _logger.LogDebug(ex, "GetServices probe failed at {Address}; falling back to Media1", address);
    }
    return null;
  }

  private async Task<(List<StreamProfile> Streams, bool HasPtz)> ProbeProfilesAsync(
    string? media2Uri, string mediaUri, string address,
    Credentials credentials, CancellationToken ct)
  {
    var streams = new List<StreamProfile>();
    var hasPtz = false;

    if (media2Uri != null)
    {
      try
      {
        var profiles = await _media.GetProfilesV2Async(media2Uri, credentials, ct);
        for (var i = 0; i < profiles.Count; i++)
        {
          var uri = await _media.GetStreamUriV2Async(media2Uri, credentials, profiles[i].Token, ct);
          if (uri == null) continue;
          streams.Add(MediaService.ToStreamProfile(profiles[i], RewriteHostOnly(uri, address), i));
          if (profiles[i].HasPtzBinding) hasPtz = true;
        }
        if (streams.Count > 0) return (streams, hasPtz);
      }
      catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Xml.XmlException or SoapFaultException)
      {
        _logger.LogWarning(ex, "Media2 probe failed at {Address}; falling back to Media1", media2Uri);
      }
    }

    var v1 = await _media.GetProfilesAsync(mediaUri, credentials, ct);
    for (var i = 0; i < v1.Count; i++)
    {
      var uri = await _media.GetStreamUriAsync(mediaUri, credentials, v1[i].Token, ct);
      if (uri == null) continue;
      streams.Add(MediaService.ToStreamProfile(v1[i], RewriteHostOnly(uri, address), i));
      if (v1[i].HasPtzBinding) hasPtz = true;
    }
    return (streams, hasPtz);
  }

  public async Task<IEventSubscription?> SubscribeEventsAsync(
    CameraConfiguration config, CancellationToken ct)
  {
    if (!config.Capabilities.Contains("events")) return null;

    var eventsUri = config.Config.GetValueOrDefault("eventsUri");
    if (eventsUri == null) return null;

    var credentials = config.Credentials
      ?? Credentials.FromUserPass(
        _config.Get("username", "admin"),
        _config.Get("password", ""));

    var pullPoint = await _events.CreatePullPointAsync(eventsUri, credentials, ct);

    Guid.TryParse(config.Config.GetValueOrDefault("cameraId"), out var cameraId);

    return new OnvifEventSubscription(
      _events,
      pullPoint.SubscriptionUri,
      credentials,
      cameraId,
      pullPoint.TerminationTime);
  }

  private static string? ReverseLookup(string address)
  {
    try
    {
      if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return null;
      if (!IPAddress.TryParse(uri.Host, out var ip)) return null;
      var entry = Dns.GetHostEntry(ip);
      if (entry.HostName != uri.Host) return entry.HostName;
    }
    catch
    {
    }
    return null;
  }

  private static string RewriteServiceUri(string serviceUri, string deviceAddress)
  {
    if (!Uri.TryCreate(deviceAddress, UriKind.Absolute, out var device))
      return serviceUri;
    if (!Uri.TryCreate(serviceUri, UriKind.Absolute, out var service))
      return serviceUri;
    var builder = new UriBuilder(service)
    {
      Host = device.Host,
      Port = device.Port
    };
    return builder.Uri.AbsoluteUri;
  }

  private static string RewriteHostOnly(string serviceUri, string deviceAddress)
  {
    if (!Uri.TryCreate(deviceAddress, UriKind.Absolute, out var device))
      return serviceUri;
    if (!Uri.TryCreate(serviceUri, UriKind.Absolute, out var service))
      return serviceUri;
    var builder = new UriBuilder(service) { Host = device.Host };
    return builder.Uri.AbsoluteUri;
  }

  private Credentials ResolveCredentials(DiscoveryOptions options) =>
    Credentials.FromUserPass(
      options.Username ?? _config.Get("username", "admin"),
      options.Password ?? _config.Get("password", ""));
}
