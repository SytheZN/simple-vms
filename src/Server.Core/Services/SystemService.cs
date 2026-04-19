using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Server.Core.PortForwarding;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class SystemService
{
  private const int UpnpPortMin = 20000;
  private const int UpnpPortMax = 60000;

  private static readonly Regex HostnameRegex = new(
    @"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$",
    RegexOptions.Compiled);

  private readonly IPluginHost _plugins;
  private readonly SystemHealth _health;
  private readonly ServerEndpoints _endpoints;
  private readonly IPortForwardingApplier _portForwarding;
  private readonly IHttpClientFactory _httpFactory;
  private readonly ILogger<SystemService> _logger;
  private readonly SemaphoreSlim _recomputeLock = new(1, 1);

  public SystemService(
    IPluginHost plugins, SystemHealth health,
    ServerEndpoints endpoints, IPortForwardingApplier portForwarding,
    IHttpClientFactory httpFactory, ILogger<SystemService> logger)
  {
    _plugins = plugins;
    _health = health;
    _endpoints = endpoints;
    _portForwarding = portForwarding;
    _httpFactory = httpFactory;
    _logger = logger;
  }

  public Task<HealthResponse> GetHealthAsync(CancellationToken ct) =>
    Task.FromResult(new HealthResponse
    {
      Status = _health.Status,
      Uptime = _health.Uptime,
      Version = _health.Version,
      TunnelPort = _endpoints.TunnelPort,
      MissingSettings = _health.MissingSettings
    });

  public async Task RecomputeMissingSettingsAsync(CancellationToken ct)
  {
    await _recomputeLock.WaitAsync(ct);
    try
    {
      OneOf<IReadOnlyDictionary<string, string>, Error> all;
      try
      {
        all = await _plugins.DataProvider.Config.GetAllAsync("server", ct);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex,
          "RecomputeMissingSettings: data provider read threw; leaving cached state in place");
        return;
      }

      if (all.IsT1)
      {
        _logger.LogWarning(
          "RecomputeMissingSettings: data provider read returned error {Error}; leaving cached state in place",
          all.AsT1.Message);
        return;
      }

      _health.SetMissingSettings(ComputeMissingSettings(all.AsT0));
    }
    finally
    {
      _recomputeLock.Release();
    }
  }

  public async Task<OneOf<VerifyRemoteAddressResponse, Error>> VerifyRemoteAddressAsync(
    string? host, CancellationToken ct)
  {
    string publicIp;
    try
    {
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      cts.CancelAfter(TimeSpan.FromSeconds(3));
      var http = _httpFactory.CreateClient();
      var json = await http.GetStringAsync("https://api.ipify.org?format=json", cts.Token);
      using var doc = JsonDocument.Parse(json);
      publicIp = doc.RootElement.GetProperty("ip").GetString()
        ?? throw new InvalidOperationException("ipify response missing 'ip' field");
    }
    catch (Exception ex)
    {
      return Error.Create(ModuleIds.SystemManagement, 0x0030, Result.Unavailable,
        $"Public IP lookup failed: {ex.Message}");
    }

    var trimmed = host?.Trim();
    if (string.IsNullOrEmpty(trimmed))
      return new VerifyRemoteAddressResponse { PublicIp = publicIp };
    if (IPAddress.TryParse(trimmed, out _))
      return new VerifyRemoteAddressResponse { PublicIp = publicIp, ResolvedIps = [trimmed] };

    try
    {
      var addresses = await Dns.GetHostAddressesAsync(
        trimmed, System.Net.Sockets.AddressFamily.InterNetwork, ct);
      return new VerifyRemoteAddressResponse
      {
        PublicIp = publicIp,
        ResolvedIps = [.. addresses.Select(a => a.ToString())]
      };
    }
    catch (Exception ex)
    {
      return Error.Create(ModuleIds.SystemManagement, 0x0031, Result.Unavailable,
        $"DNS lookup for '{trimmed}' failed: {ex.Message}");
    }
  }

  public async Task<OneOf<StorageResponse, Error>> GetStorageAsync(CancellationToken ct)
  {
    IReadOnlyList<StorageBreakdownItem>? breakdown = null;
    try
    {
      var result = await _plugins.DataProvider.Segments.GetSizeBreakdownAsync(ct);
      if (result.IsT0)
        breakdown = result.AsT0.Select(u => new StorageBreakdownItem
        {
          CameraId = u.CameraId,
          CameraName = u.CameraName,
          StreamProfile = u.StreamProfile,
          SizeBytes = u.SizeBytes,
          DurationMicros = u.DurationMicros
        }).ToList();
    }
    catch {}

    var stores = new List<StorageStoreDto>();
    foreach (var provider in _plugins.StorageProviders)
    {
      try
      {
        var stats = await provider.GetStatsAsync(ct);
        stores.Add(new StorageStoreDto
        {
          TotalBytes = stats.TotalBytes,
          UsedBytes = stats.UsedBytes,
          FreeBytes = stats.FreeBytes,
          RecordingBytes = stats.RecordingBytes,
          Breakdown = breakdown
        });
      }
      catch
      {
        stores.Add(new StorageStoreDto
        {
          TotalBytes = -1,
          UsedBytes = -1,
          FreeBytes = -1,
          RecordingBytes = -1
        });
      }
    }

    return new StorageResponse { Stores = stores };
  }

  public async Task<OneOf<ServerSettings, Error>> GetSettingsAsync(CancellationToken ct)
  {
    var all = await _plugins.DataProvider.Config.GetAllAsync("server", ct);
    if (all.IsT1) return all.AsT1;

    var settings = all.AsT0;
    var mode = InferMode(settings);

    return new ServerSettings
    {
      ServerName = settings.GetValueOrDefault("server.name"),
      InternalEndpoint = settings.GetValueOrDefault("server.internalEndpoint"),
      Mode = mode,
      ExternalHost = settings.GetValueOrDefault("server.externalHost"),
      ExternalPort = int.TryParse(settings.GetValueOrDefault("server.externalPort"), out var ep) ? ep : null,
      UpnpRouterAddress = settings.GetValueOrDefault("server.upnp.routerAddress"),
      SegmentDuration = int.TryParse(settings.GetValueOrDefault("server.segmentDuration"), out var sd)
        ? sd : null,
      DiscoverySubnets = settings.GetValueOrDefault("server.discoverySubnets")
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
      LegacyExternalEndpoint = settings.GetValueOrDefault("server.externalEndpoint"),
      PortForwardingStatus = mode == RemoteAccessMode.Upnp ? _portForwarding.GetStatus() : null
    };
  }

  public async Task<OneOf<Success, Error>> UpdateSettingsAsync(
    ServerSettings request, CancellationToken ct)
  {
    var all = await _plugins.DataProvider.Config.GetAllAsync("server", ct);
    if (all.IsT1) return all.AsT1;
    var persisted = all.AsT0;
    var persistedMode = InferMode(persisted);
    var effectiveMode = request.Mode ?? persistedMode;

    var touchesRemoteFields =
      request.ExternalHost != null || request.ExternalPort.HasValue || request.UpnpRouterAddress != null;

    if (request.InternalEndpoint != null)
    {
      var validation = ValidateInternalEndpoint(request.InternalEndpoint);
      if (validation.IsT1) return validation.AsT1;
    }

    if (request.Mode.HasValue || touchesRemoteFields)
    {
      if (!effectiveMode.HasValue)
        return Error.Create(ModuleIds.SystemManagement, 0x001B, Result.BadRequest,
          "Remote access mode must be specified");

      var merged = new ServerSettings
      {
        Mode = effectiveMode,
        ExternalHost = request.ExternalHost ?? persisted.GetValueOrDefault("server.externalHost"),
        ExternalPort = request.ExternalPort
          ?? (int.TryParse(persisted.GetValueOrDefault("server.externalPort"), out var pp) ? pp : null),
        UpnpRouterAddress = request.UpnpRouterAddress ?? persisted.GetValueOrDefault("server.upnp.routerAddress")
      };
      var modeValidation = ValidateModeFields(merged);
      if (modeValidation.IsT1) return modeValidation.AsT1;
    }

    var config = _plugins.DataProvider.Config;

    if (request.ServerName != null)
    {
      var r = await config.SetAsync("server", "server.name", request.ServerName, ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.InternalEndpoint != null)
    {
      var r = await config.SetAsync("server", "server.internalEndpoint", request.InternalEndpoint, ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.SegmentDuration.HasValue)
    {
      var r = await config.SetAsync("server", "server.segmentDuration",
        request.SegmentDuration.Value.ToString(), ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.DiscoverySubnets != null)
    {
      var r = await config.SetAsync("server", "server.discoverySubnets",
        string.Join(',', request.DiscoverySubnets), ct);
      if (r.IsT1) return r.AsT1;
    }

    if (request.ExternalHost != null)
    {
      var r = await config.SetAsync("server", "server.externalHost", request.ExternalHost, ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.ExternalPort.HasValue)
    {
      var r = await config.SetAsync("server", "server.externalPort",
        request.ExternalPort.Value.ToString(), ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.UpnpRouterAddress != null)
    {
      var r = await config.SetAsync("server", "server.upnp.routerAddress",
        request.UpnpRouterAddress, ct);
      if (r.IsT1) return r.AsT1;
    }

    if (request.Mode.HasValue)
    {
      var mode = request.Mode.Value;
      var r = await config.SetAsync("server", "server.remoteAccess.mode",
        mode.ToString().ToLowerInvariant(), ct);
      if (r.IsT1) return r.AsT1;

      switch (mode)
      {
        case RemoteAccessMode.None:
          await config.DeleteAsync("server", "server.externalHost", ct);
          await config.DeleteAsync("server", "server.externalPort", ct);
          await config.DeleteAsync("server", "server.upnp.routerAddress", ct);
          break;
        case RemoteAccessMode.Manual:
          await config.DeleteAsync("server", "server.upnp.routerAddress", ct);
          break;
      }

      await config.DeleteAsync("server", "server.externalEndpoint", ct);
      await config.DeleteAsync("server", "server.upnp.enabled", ct);
    }

    if (request.Mode.HasValue || touchesRemoteFields)
    {
      await config.DeleteAsync("server", "server.portForwarding.protocol", ct);
      var applyResult = await _portForwarding.ApplyAsync(ct);
      if (applyResult.IsT1) return applyResult.AsT1;
    }

    await RecomputeMissingSettingsAsync(ct);

    return new Success();
  }

  internal static RemoteAccessMode? InferMode(IReadOnlyDictionary<string, string> settings)
  {
    var explicitMode = settings.GetValueOrDefault("server.remoteAccess.mode");
    if (!string.IsNullOrWhiteSpace(explicitMode)
        && Enum.TryParse<RemoteAccessMode>(explicitMode, ignoreCase: true, out var parsed))
      return parsed;

    if (bool.TryParse(settings.GetValueOrDefault("server.upnp.enabled"), out var upnp) && upnp)
      return RemoteAccessMode.Upnp;

    if (!string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.externalHost"))
        && !string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.externalPort")))
      return RemoteAccessMode.Manual;

    if (!string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.externalEndpoint")))
      return null;

    return RemoteAccessMode.None;
  }

  internal static string[]? ComputeMissingSettings(IReadOnlyDictionary<string, string> settings)
  {
    var missing = new List<string>();

    if (string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.internalEndpoint")))
      missing.Add("internalEndpoint");

    if (!string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.externalEndpoint")))
      missing.Add("legacyExternalEndpoint");

    var mode = InferMode(settings);
    if (mode == RemoteAccessMode.Upnp)
    {
      if (string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.externalHost")))
        missing.Add("externalHost");
      if (string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.externalPort")))
        missing.Add("externalPort");
      if (string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.upnp.routerAddress")))
        missing.Add("upnpRouterAddress");
    }
    else if (mode == RemoteAccessMode.Manual)
    {
      if (string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.externalHost")))
        missing.Add("externalHost");
      if (string.IsNullOrWhiteSpace(settings.GetValueOrDefault("server.externalPort")))
        missing.Add("externalPort");
    }

    return missing.Count > 0 ? [.. missing] : null;
  }

  internal static OneOf<Success, Error> ValidateInternalEndpoint(string endpoint) =>
    ValidateHostOrIp(endpoint, allowPort: true, fieldLabel: "Internal endpoint");

  internal static OneOf<Success, Error> ValidateExternalHost(string host) =>
    ValidateHostOrIp(host, allowPort: false, fieldLabel: "External host");

  internal static OneOf<Success, Error> ValidateRouterAddress(string address)
  {
    if (string.IsNullOrWhiteSpace(address))
      return Error.Create(ModuleIds.SystemManagement, 0x0040, Result.BadRequest,
        "Router address cannot be empty");

    if (IPAddress.TryParse(address, out var ip))
    {
      if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        return Error.Create(ModuleIds.SystemManagement, 0x0041, Result.BadRequest,
          "Router address must be an IPv4 literal or a hostname");
      return new Success();
    }

    if (!HostnameRegex.IsMatch(address))
      return Error.Create(ModuleIds.SystemManagement, 0x0042, Result.BadRequest,
        "Router address must be an IPv4 literal or a hostname");

    return new Success();
  }

  internal static OneOf<Success, Error> ValidateExternalPort(int port, RemoteAccessMode mode) => mode switch
  {
    RemoteAccessMode.Upnp when port < UpnpPortMin || port > UpnpPortMax =>
      Error.Create(ModuleIds.SystemManagement, 0x0043, Result.BadRequest,
        $"External port must be between {UpnpPortMin} and {UpnpPortMax} in Automatic mode"),
    RemoteAccessMode.Manual when port < 1 || port > 65535 =>
      Error.Create(ModuleIds.SystemManagement, 0x0044, Result.BadRequest,
        "External port must be between 1 and 65535"),
    _ => new Success()
  };

  private static OneOf<Success, Error> ValidateModeFields(ServerSettings r)
  {
    var mode = r.Mode!.Value;

    if (mode == RemoteAccessMode.None)
    {
      if (r.ExternalHost != null)
        return Error.Create(ModuleIds.SystemManagement, 0x0045, Result.BadRequest,
          "External host is not allowed when remote access is disabled");
      if (r.ExternalPort.HasValue)
        return Error.Create(ModuleIds.SystemManagement, 0x0046, Result.BadRequest,
          "External port is not allowed when remote access is disabled");
      if (r.UpnpRouterAddress != null)
        return Error.Create(ModuleIds.SystemManagement, 0x0047, Result.BadRequest,
          "Router address is not allowed when remote access is disabled");
      return new Success();
    }

    if (string.IsNullOrWhiteSpace(r.ExternalHost))
      return Error.Create(ModuleIds.SystemManagement, 0x0048, Result.BadRequest,
        "External host is required");
    var hostResult = ValidateExternalHost(r.ExternalHost);
    if (hostResult.IsT1) return hostResult.AsT1;

    if (!r.ExternalPort.HasValue)
      return Error.Create(ModuleIds.SystemManagement, 0x0049, Result.BadRequest,
        "External port is required");
    var portResult = ValidateExternalPort(r.ExternalPort.Value, mode);
    if (portResult.IsT1) return portResult.AsT1;

    if (mode == RemoteAccessMode.Upnp)
    {
      if (string.IsNullOrWhiteSpace(r.UpnpRouterAddress))
        return Error.Create(ModuleIds.SystemManagement, 0x004A, Result.BadRequest,
          "Router address is required for UPnP");
      var routerResult = ValidateRouterAddress(r.UpnpRouterAddress);
      if (routerResult.IsT1) return routerResult.AsT1;
    }
    else if (mode == RemoteAccessMode.Manual && r.UpnpRouterAddress != null)
    {
      return Error.Create(ModuleIds.SystemManagement, 0x004B, Result.BadRequest,
        "Router address is only used with UPnP");
    }

    return new Success();
  }

  internal static OneOf<Success, Error> ValidateHostOrIp(string value, bool allowPort, string fieldLabel)
  {
    if (string.IsNullOrWhiteSpace(value))
      return Error.Create(ModuleIds.SystemManagement, 0x004C, Result.BadRequest,
        $"{fieldLabel} cannot be empty");

    var host = value;

    if (IPAddress.TryParse(value, out var rawIp))
    {
      host = rawIp.ToString();
    }
    else if (value.StartsWith('['))
    {
      var close = value.IndexOf(']');
      if (close < 0)
        return Error.Create(ModuleIds.SystemManagement, 0x004D, Result.BadRequest,
          $"{fieldLabel} has a malformed IPv6 literal");

      host = value[1..close];
      if (!IPAddress.TryParse(host, out _))
        return Error.Create(ModuleIds.SystemManagement, 0x004E, Result.BadRequest,
          $"{fieldLabel} is not a valid IPv6 address");

      if (close + 1 < value.Length)
      {
        if (!allowPort)
          return Error.Create(ModuleIds.SystemManagement, 0x004F, Result.BadRequest,
            $"{fieldLabel} must not include a port");
        var rest = value[(close + 1)..];
        if (!rest.StartsWith(':')
            || !int.TryParse(rest[1..], out var port6) || port6 < 1 || port6 > 65535)
          return Error.Create(ModuleIds.SystemManagement, 0x0050, Result.BadRequest,
            $"{fieldLabel} port must be between 1 and 65535");
      }
    }
    else
    {
      if (value.IndexOf(':') != value.LastIndexOf(':'))
        return Error.Create(ModuleIds.SystemManagement, 0x0057, Result.BadRequest,
          $"{fieldLabel} IPv6 literals must be enclosed in brackets, e.g. [fe80::1]");

      var colon = value.LastIndexOf(':');
      if (colon >= 0)
      {
        if (!allowPort)
          return Error.Create(ModuleIds.SystemManagement, 0x0051, Result.BadRequest,
            $"{fieldLabel} must not include a port");

        host = value[..colon];
        if (!int.TryParse(value[(colon + 1)..], out var port) || port < 1 || port > 65535)
          return Error.Create(ModuleIds.SystemManagement, 0x0052, Result.BadRequest,
            $"{fieldLabel} port must be between 1 and 65535");
      }
    }

    if (string.IsNullOrWhiteSpace(host))
      return Error.Create(ModuleIds.SystemManagement, 0x0053, Result.BadRequest,
        $"{fieldLabel} host cannot be empty");

    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
      return Error.Create(ModuleIds.SystemManagement, 0x0054, Result.BadRequest,
        $"{fieldLabel} '{host}' is not reachable from other devices on your network");

    if (IPAddress.TryParse(host, out var ip)
        && (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal))
      return Error.Create(ModuleIds.SystemManagement, 0x0055, Result.BadRequest,
        $"{fieldLabel} '{host}' is not reachable from other devices on your network");

    if (!IPAddress.TryParse(host, out _) && !HostnameRegex.IsMatch(host))
      return Error.Create(ModuleIds.SystemManagement, 0x0056, Result.BadRequest,
        $"{fieldLabel} '{host}' is not a valid hostname or IP address");

    return new Success();
  }
}
