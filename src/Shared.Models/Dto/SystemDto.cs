namespace Shared.Models.Dto;

public sealed class HealthResponse
{
  public required string Status { get; init; }
  public required int Uptime { get; init; }
  public required CameraHealthCounts Cameras { get; init; }
  public required StorageResponse Storage { get; init; }
  public required string Version { get; init; }
}

public sealed class CameraHealthCounts
{
  public required int Total { get; init; }
  public required int Online { get; init; }
  public required int Offline { get; init; }
  public required int Error { get; init; }
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
}

public sealed class ServerSettings
{
  public string? ServerName { get; init; }
  public string? ExternalEndpoint { get; init; }
  public int? SegmentDuration { get; init; }
  public string[]? DiscoverySubnets { get; init; }
  public CredentialsDto? DefaultCredentials { get; init; }
}
