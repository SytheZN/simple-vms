using Client.Core.Events;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging;

namespace Client.Core;

public enum AutoConnectStatus
{
  NoCredentials,
  TunnelFailed,
  Connected,
}

public readonly record struct AutoConnectOutcome(AutoConnectStatus Status, int ConnectedAddressIndex);

public sealed class ClientLifecycleService
{
  private readonly ICredentialStore _credentials;
  private readonly ITunnelService _tunnel;
  private readonly IEventService _events;
  private readonly NotificationRouter _router;
  private readonly ILogger<ClientLifecycleService> _logger;

  public ClientLifecycleService(
    ICredentialStore credentials,
    ITunnelService tunnel,
    IEventService events,
    NotificationRouter router,
    ILogger<ClientLifecycleService> logger)
  {
    _credentials = credentials;
    _tunnel = tunnel;
    _events = events;
    _router = router;
    _logger = logger;
  }

  public async Task<AutoConnectOutcome> AutoConnectAsync(
    ConnectionOptions options,
    IReadOnlyList<NotificationRule> notificationRules,
    CancellationToken ct)
  {
    var creds = await _credentials.LoadAsync();
    if (creds == null)
    {
      _logger.LogInformation("No stored credentials, skipping auto-connect");
      return new AutoConnectOutcome(AutoConnectStatus.NoCredentials, -1);
    }

    _logger.LogInformation("Credentials loaded for client {ClientId}, {AddressCount} address(es)",
      creds.ClientId, creds.Addresses.Length);

    _router.UpdateRules(notificationRules);

    _logger.LogInformation("Connecting to tunnel (preferred index {Index}, reprobe {Reprobe})",
      options.LastSuccessfulIndex, options.ReprobeEnabled);
    try { await _tunnel.ConnectAsync(options, ct); }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Auto-connect failed");
      return new AutoConnectOutcome(AutoConnectStatus.TunnelFailed, -1);
    }

    _logger.LogInformation("Tunnel connected at index {Index}, starting event service",
      _tunnel.ConnectedAddressIndex);
    try { await _events.StartAsync(ct); }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Event service failed to start after tunnel connect");
    }
    return new AutoConnectOutcome(AutoConnectStatus.Connected, _tunnel.ConnectedAddressIndex);
  }

  public async Task ShutdownAsync(CancellationToken ct)
  {
    _logger.LogInformation("Shutdown starting");
    await _events.StopAsync(ct);
    _logger.LogDebug("Event service stopped");
    await _tunnel.DisconnectAsync(ct);
    _logger.LogDebug("Tunnel disconnected");
    _logger.LogInformation("Shutdown complete");
  }
}
