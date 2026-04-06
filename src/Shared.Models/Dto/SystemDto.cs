namespace Shared.Models.Dto;

public sealed class HealthResponse
{
  public required string Status { get; init; }
  public required int Uptime { get; init; }
  public required string Version { get; init; }
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

public sealed class ServerSettings
{
  public string? ServerName { get; init; }
  public string? ExternalEndpoint { get; init; }
  public int? SegmentDuration { get; init; }
  public string[]? DiscoverySubnets { get; init; }
}
