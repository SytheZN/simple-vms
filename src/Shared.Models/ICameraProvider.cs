namespace Shared.Models;

public interface ICameraProvider
{
  string ProviderId { get; }
  Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(DiscoveryOptions options, CancellationToken ct);
  Task<CameraConfiguration> ConfigureAsync(string address, Credentials credentials, CancellationToken ct);
  Task<IEventSubscription?> SubscribeEventsAsync(CameraConfiguration config, CancellationToken ct);
}
