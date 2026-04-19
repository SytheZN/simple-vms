using System.Text.Json.Serialization;

namespace Shared.Models.Dto;

public sealed class HealthResponse
{
  public required string Status { get; init; }
  public required int Uptime { get; init; }
  public required string Version { get; init; }
  public required int TunnelPort { get; init; }
  public string[]? MissingSettings { get; init; }
}

public sealed class VerifyRemoteAddressResponse
{
  public required string PublicIp { get; init; }
  public string[]? ResolvedIps { get; init; }
}

public sealed class StorageResponse
{
  public required IReadOnlyList<StorageStoreDto> Stores { get; init; }
}

public sealed class StorageStoreDto
{
  public required long TotalBytes { get; init; }
  public required long UsedBytes { get; init; }
  public required long FreeBytes { get; init; }
  public required long RecordingBytes { get; init; }
  public IReadOnlyList<StorageBreakdownItem>? Breakdown { get; init; }
}

public sealed class StorageBreakdownItem
{
  public required Guid CameraId { get; init; }
  public required string CameraName { get; init; }
  public required string StreamProfile { get; init; }
  public required long SizeBytes { get; init; }
  public required ulong DurationMicros { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<RemoteAccessMode>))]
public enum RemoteAccessMode
{
  [JsonStringEnumMemberName("none")] None,
  [JsonStringEnumMemberName("manual")] Manual,
  [JsonStringEnumMemberName("upnp")] Upnp
}

public sealed class PortForwardingStatus
{
  public required bool Active { get; init; }
  public string? Protocol { get; init; }
  public int? ExternalPort { get; init; }
  public int? InternalPort { get; init; }
  public string? LastError { get; init; }
  public ulong? LastAppliedAtMicros { get; init; }
}

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
  public PortForwardingStatus? PortForwardingStatus { get; init; }
}
