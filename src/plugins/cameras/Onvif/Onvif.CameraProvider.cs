using System.Net;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
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
      var scannedAddresses = await WsDiscovery.ScanSubnetsAsync(_http, options.Subnets, ct);
      foreach (var addr in scannedAddresses)
        allAddresses.Add(addr);
    }

    var cameras = new List<DiscoveredCamera>();
    foreach (var address in allAddresses)
    {
      var hostname = ReverseLookup(address);
      try
      {
        var info = await _device.GetDeviceInformationAsync(address, credentials, ct);
        cameras.Add(new DiscoveredCamera
        {
          Address = address,
          Hostname = hostname,
          Name = info.Model != null ? $"{info.Manufacturer} {info.Model}" : info.Manufacturer,
          Manufacturer = info.Manufacturer,
          Model = info.Model,
          ProviderId = ProviderId
        });
      }
      catch
      {
        cameras.Add(new DiscoveredCamera
        {
          Address = address,
          Hostname = hostname,
          ProviderId = ProviderId
        });
      }
    }

    return cameras;
  }

  public async Task<CameraConfiguration> ConfigureAsync(
    string address, Credentials credentials, CancellationToken ct)
  {
    var info = await _device.GetDeviceInformationAsync(address, credentials, ct);
    var caps = await _device.GetCapabilitiesAsync(address, credentials, ct);

    var mediaUri = caps.MediaUri ?? address.Replace("/device_service", "/media_service");

    var profiles = await _media.GetProfilesAsync(mediaUri, credentials, ct);
    var streams = new List<StreamProfile>();

    for (var i = 0; i < profiles.Count; i++)
    {
      var uri = await _media.GetStreamUriAsync(mediaUri, credentials, profiles[i].Token, ct);
      if (uri == null) continue;
      streams.Add(MediaService.ToStreamProfile(profiles[i], uri, i));
    }

    var capabilities = new List<string>();
    if (caps.HasPtz) capabilities.Add("ptz");
    if (caps.HasAudio) capabilities.Add("audio");
    if (caps.HasEvents) capabilities.Add("events");

    var config = new Dictionary<string, string>
    {
      ["deviceUri"] = address,
      ["serialNumber"] = info.SerialNumber ?? "",
      ["firmwareVersion"] = info.FirmwareVersion ?? ""
    };
    if (caps.MediaUri != null) config["mediaUri"] = caps.MediaUri;
    if (caps.EventsUri != null) config["eventsUri"] = caps.EventsUri;
    if (caps.PtzUri != null) config["ptzUri"] = caps.PtzUri;

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

  public async Task<IEventSubscription?> SubscribeEventsAsync(
    CameraConfiguration config, CancellationToken ct)
  {
    if (!config.Capabilities.Contains("events")) return null;

    var eventsUri = config.Config.GetValueOrDefault("eventsUri");
    if (eventsUri == null) return null;

    var credentials = new Credentials
    {
      Username = _config.Get("username", "admin"),
      Password = _config.Get("password", "")
    };

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

  private Credentials ResolveCredentials(DiscoveryOptions options) => new()
  {
    Username = options.Username ?? _config.Get("username", "admin"),
    Password = options.Password ?? _config.Get("password", "")
  };
}
