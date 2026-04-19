using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Core;
using Server.Core.Services;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.PortForwarding;

public sealed class PortForwardingService : IHostedService, IAsyncDisposable, IPortForwardingApplier
{
  private const uint LeaseSeconds = 3600;
  private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(60);
  private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(10);

  private readonly IPluginHost _plugins;
  private readonly ServerEndpoints _endpoints;
  private readonly SystemHealth _health;
  private readonly ILogger<PortForwardingService> _logger;
  private readonly IHttpClientFactory _httpFactory;
  private readonly SemaphoreSlim _controlLock = new(1, 1);

  private CancellationToken _hostCt;
  private CancellationTokenSource? _loopCts;
  private Task? _loopTask;
  private bool _stopped;
  private Snapshot _status = new(null, null, null);

  private sealed record Snapshot(ActiveMapping? Mapping, string? LastError, ulong? LastAppliedAtMicros);

  internal enum MappingProtocol { NatPmp, Upnp }

  private sealed record ActiveMapping(
    MappingProtocol Protocol,
    IPAddress RouterIp,
    ushort ExternalPort,
    ushort InternalPort,
    IgdEndpoint? Igd);

  public PortForwardingService(
    IPluginHost plugins, ServerEndpoints endpoints,
    SystemHealth health,
    ILogger<PortForwardingService> logger,
    IHttpClientFactory httpFactory)
  {
    _plugins = plugins;
    _endpoints = endpoints;
    _health = health;
    _logger = logger;
    _httpFactory = httpFactory;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _hostCt = cancellationToken;
    StartLoop();
    return Task.CompletedTask;
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    await _controlLock.WaitAsync(cancellationToken);
    try
    {
      _stopped = true;
      await StopLoopAsync();
      await RemoveActiveMappingAsync(CancellationToken.None);
    }
    finally { _controlLock.Release(); }
  }

  public ValueTask DisposeAsync()
  {
    _loopCts?.Dispose();
    _controlLock.Dispose();
    return ValueTask.CompletedTask;
  }

  public async Task<OneOf<Success, Error>> ApplyAsync(CancellationToken ct)
  {
    await _controlLock.WaitAsync(ct);
    try
    {
      await StopLoopAsync();
      var result = await DoApplyAsync(ct);
      if (!_stopped) StartLoop();
      return result;
    }
    finally { _controlLock.Release(); }
  }

  public PortForwardingStatus GetStatus()
  {
    var snap = _status;
    return new PortForwardingStatus
    {
      Active = snap.Mapping != null,
      Protocol = snap.Mapping?.Protocol switch
      {
        MappingProtocol.NatPmp => "nat-pmp",
        MappingProtocol.Upnp => "upnp",
        _ => null
      },
      ExternalPort = snap.Mapping?.ExternalPort,
      InternalPort = snap.Mapping?.InternalPort,
      LastError = snap.LastError,
      LastAppliedAtMicros = snap.LastAppliedAtMicros
    };
  }

  private void StartLoop()
  {
    _loopCts = CancellationTokenSource.CreateLinkedTokenSource(_hostCt);
    _loopTask = RunLoopAsync(_loopCts.Token);
  }

  private async Task StopLoopAsync()
  {
    if (_loopCts == null) return;
    _loopCts.Cancel();
    if (_loopTask != null)
    {
      try { await _loopTask; }
      catch (OperationCanceledException) { }
    }
    _loopCts.Dispose();
    _loopCts = null;
    _loopTask = null;
  }

  private async Task<OneOf<Success, Error>> DoApplyAsync(CancellationToken ct)
  {
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(ApplyTimeout);

    OneOf<Success, Error> result;
    try
    {
      result = await ApplyCoreAsync(cts.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      result = Error.Create(ModuleIds.SystemManagement, 0x0020, Result.Unavailable,
        "Port-forwarding operation timed out after 10 seconds");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Port-forwarding apply failed with unexpected error");
      result = Error.Create(ModuleIds.SystemManagement, 0x0021, Result.InternalError,
        $"Port-forwarding apply failed: {ex.Message}");
    }

    _status = _status with
    {
      LastAppliedAtMicros = DateTimeOffset.UtcNow.ToUnixMicroseconds(),
      LastError = result.IsT1 ? result.AsT1.Message : null
    };
    return result;
  }

  private async Task<OneOf<Success, Error>> ApplyCoreAsync(CancellationToken ct)
  {
    var settings = await _plugins.DataProvider.Config.GetAllAsync("server", ct);
    if (settings.IsT1) return settings.AsT1;
    var map = settings.AsT0;

    var mode = SystemService.InferMode(map);
    if (mode != RemoteAccessMode.Upnp)
    {
      await RemoveActiveMappingAsync(ct);
      return new Success();
    }

    var internalEndpoint = map.GetValueOrDefault("server.internalEndpoint");
    if (string.IsNullOrWhiteSpace(internalEndpoint))
      return Error.Create(ModuleIds.SystemManagement, 0x0022, Result.BadRequest,
        "Port forwarding requires an internal endpoint to be configured");

    var internalIp = await ResolveInternalIpv4Async(internalEndpoint, ct);
    if (internalIp == null)
      return Error.Create(ModuleIds.SystemManagement, 0x0028, Result.BadRequest,
        $"Could not resolve '{internalEndpoint}' to an IPv4 address");

    if (!ushort.TryParse(map.GetValueOrDefault("server.externalPort"), out var externalPort))
      return Error.Create(ModuleIds.SystemManagement, 0x0023, Result.BadRequest,
        "External port is not configured");

    var routerAddress = map.GetValueOrDefault("server.upnp.routerAddress");
    if (string.IsNullOrWhiteSpace(routerAddress))
      return Error.Create(ModuleIds.SystemManagement, 0x0024, Result.BadRequest,
        "Router address is not configured");

    var routerIp = await ResolveInternalIpv4Async(routerAddress, ct);
    if (routerIp == null)
      return Error.Create(ModuleIds.SystemManagement, 0x0029, Result.BadRequest,
        $"Could not resolve router address '{routerAddress}' to an IPv4 address");

    var internalPort = (ushort)_endpoints.TunnelPort;
    var stickyProtocol = ParseStoredProtocol(map.GetValueOrDefault(ProtocolConfigKey));

    if (stickyProtocol.HasValue)
    {
      var result = stickyProtocol.Value == MappingProtocol.NatPmp
        ? await TryNatPmpAsync(routerIp, externalPort, internalPort, ct)
        : await TryUpnpAsync(routerAddress, routerIp, externalPort, internalPort, internalIp, ct);
      if (result == null)
        return Error.Create(ModuleIds.SystemManagement, 0x0025, Result.Unavailable,
          $"{FormatProtocol(stickyProtocol.Value)} did not respond at {routerAddress}. "
          + "The router may be unreachable. The mapping will retry automatically.");
      return result.Value;
    }

    foreach (var protocol in new[] { MappingProtocol.NatPmp, MappingProtocol.Upnp })
    {
      var result = protocol == MappingProtocol.NatPmp
        ? await TryNatPmpAsync(routerIp, externalPort, internalPort, ct)
        : await TryUpnpAsync(routerAddress, routerIp, externalPort, internalPort, internalIp, ct);
      if (result == null) continue;
      if (result.Value.IsT0) await PersistProtocolAsync(protocol, ct);
      return result.Value;
    }

    return Error.Create(ModuleIds.SystemManagement, 0x002A, Result.Unavailable,
      $"Neither NAT-PMP nor UPnP responded at {routerAddress}. Confirm one of them is enabled on the router.");
  }

  private static string FormatProtocol(MappingProtocol p) =>
    p == MappingProtocol.NatPmp ? "NAT-PMP" : "UPnP";

  private const string ProtocolConfigKey = "server.portForwarding.protocol";

  internal static MappingProtocol? ParseStoredProtocol(string? stored) => stored switch
  {
    "nat-pmp" => MappingProtocol.NatPmp,
    "upnp" => MappingProtocol.Upnp,
    _ => null
  };

  private async Task PersistProtocolAsync(MappingProtocol protocol, CancellationToken ct)
  {
    var value = protocol == MappingProtocol.NatPmp ? "nat-pmp" : "upnp";
    await _plugins.DataProvider.Config.SetAsync("server", ProtocolConfigKey, value, ct);
  }

  private async Task<OneOf<Success, Error>?> TryNatPmpAsync(
    IPAddress routerIp, ushort externalPort, ushort internalPort, CancellationToken ct)
  {
    var natpmp = new NatPmpClient();
    var response = await natpmp.AddMappingAsync(routerIp, internalPort, externalPort, LeaseSeconds, ct);
    if (response == null) return null;

    if (response.Code != NatPmpResultCode.Success)
    {
      _logger.LogInformation("NAT-PMP returned {Code}; falling back to UPnP", response.Code);
      return null;
    }

    if (response.ExternalPort != externalPort)
      return Error.Create(ModuleIds.SystemManagement, 0x0027, Result.BadRequest,
        $"Router could not assign external port {externalPort} via NAT-PMP " +
        $"(assigned {response.ExternalPort} instead). Pick a different port.");

    await TryRemoveStaleMappingAsync(
      keepProtocol: MappingProtocol.NatPmp,
      keepExternalPort: externalPort,
      ct);

    _status = _status with
    {
      Mapping = new ActiveMapping(MappingProtocol.NatPmp, routerIp, externalPort, internalPort, null)
    };
    _logger.LogInformation(
      "NAT-PMP mapping active: external {ExternalPort} -> {RouterIp} -> {InternalPort} (lease {Lease}s)",
      externalPort, routerIp, internalPort, response.Lifetime);

    return new Success();
  }

  private async Task<OneOf<Success, Error>?> TryUpnpAsync(
    string routerAddress, IPAddress routerIp, ushort externalPort, ushort internalPort,
    IPAddress internalIp, CancellationToken ct)
  {
    var http = _httpFactory.CreateClient("upnp");
    var discovery = new IgdDiscovery(http);
    var igd = await discovery.FromRouterAddressAsync(routerAddress, ct);
    if (igd == null) return null;

    await TryRemoveStaleMappingAsync(
      keepProtocol: MappingProtocol.Upnp,
      keepExternalPort: externalPort,
      ct);

    var client = new UpnpClient(http, igd.ControlUrl, igd.ServiceType);
    try
    {
      await client.AddPortMappingAsync(
        externalPort, internalPort, internalIp.ToString(),
        LeaseSeconds, "Simple VMS tunnel", ct);
    }
    catch (UpnpSoapFaultException ex)
    {
      _logger.LogError(ex, "UPnP AddPortMapping rejected: {Fault}", ex.RawFault);
      var detail = ex.ErrorDescription ?? ex.Message;
      var prefix = ex.ErrorCode.HasValue ? $"{ex.ErrorCode.Value}: " : string.Empty;
      return Error.Create(ModuleIds.SystemManagement, 0x0026, Result.BadRequest,
        $"Router rejected port mapping ({prefix}{detail})");
    }

    _status = _status with
    {
      Mapping = new ActiveMapping(MappingProtocol.Upnp, routerIp, externalPort, internalPort, igd)
    };
    _logger.LogInformation(
      "UPnP mapping active: external {ExternalPort} -> {InternalIp}:{InternalPort} (lease {Lease}s)",
      externalPort, internalIp, internalPort, LeaseSeconds);

    return new Success();
  }

  private async Task TryRemoveStaleMappingAsync(
    MappingProtocol keepProtocol, ushort keepExternalPort, CancellationToken ct)
  {
    var mapping = _status.Mapping;
    if (mapping == null) return;
    if (mapping.Protocol == keepProtocol && mapping.ExternalPort == keepExternalPort) return;
    await RemoveActiveMappingAsync(ct);
  }

  private async Task RunLoopAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      if (_health.Status == "healthy")
      {
        try
        {
          var result = await DoApplyAsync(ct);
          if (result.IsT1)
            _logger.LogWarning("Port-forwarding reconcile: {Error}", result.AsT1.Message);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Port-forwarding reconcile loop error");
        }
      }

      try { await Task.Delay(ReconcileInterval, ct); }
      catch (OperationCanceledException) { break; }
    }
  }

  private static async Task<IPAddress?> ResolveInternalIpv4Async(
    string endpoint, CancellationToken ct)
  {
    var host = HostPort.SplitHost(endpoint);

    if (IPAddress.TryParse(host, out var ip)
        && ip.AddressFamily == AddressFamily.InterNetwork)
      return ip;

    try
    {
      var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
      return addresses.FirstOrDefault();
    }
    catch
    {
      return null;
    }
  }

  private async Task RemoveActiveMappingAsync(CancellationToken ct)
  {
    var mapping = _status.Mapping;
    if (mapping == null) return;
    _status = _status with { Mapping = null };

    try
    {
      switch (mapping.Protocol)
      {
        case MappingProtocol.NatPmp:
          await new NatPmpClient().DeleteMappingAsync(mapping.RouterIp, mapping.InternalPort, ct);
          _logger.LogInformation(
            "NAT-PMP mapping removed for internal port {Port}", mapping.InternalPort);
          break;

        case MappingProtocol.Upnp when mapping.Igd != null:
          var http = _httpFactory.CreateClient("upnp");
          var client = new UpnpClient(http, mapping.Igd.ControlUrl, mapping.Igd.ServiceType);
          await client.DeletePortMappingAsync(mapping.ExternalPort, ct);
          _logger.LogInformation(
            "UPnP mapping removed for external port {Port}", mapping.ExternalPort);
          break;
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to remove {Protocol} mapping", mapping.Protocol);
    }

    try
    {
      await _plugins.DataProvider.Config.DeleteAsync("server", ProtocolConfigKey, ct);
    }
    catch { }
  }
}
