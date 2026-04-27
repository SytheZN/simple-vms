namespace Shared.Api;

public sealed class ServerSettings
{
  public string? ServerName { get; init; }
  public string? InternalEndpoint { get; init; }
  public RemoteAccessMode? Mode { get; init; }
  public string? ExternalHost { get; init; }
  public int? ExternalPort { get; init; }
  public string? UpnpRouterAddress { get; init; }
  public int? SegmentDuration { get; init; }
  public string[]? DiscoverySubnets { get; init; }
  public string? LegacyExternalEndpoint { get; init; }
  public PortForwardingStatusDto? PortForwardingStatus { get; init; }
}
