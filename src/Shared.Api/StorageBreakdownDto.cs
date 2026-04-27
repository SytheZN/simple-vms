namespace Shared.Api;

public sealed class StorageBreakdownDto
{
  public required Guid CameraId { get; init; }
  public required string CameraName { get; init; }
  public required string StreamProfile { get; init; }
  public required long SizeBytes { get; init; }
  public required ulong DurationMicros { get; init; }
}
