namespace Shared.Api;

public sealed class RecordingSegmentDto
{
  public required Guid Id { get; init; }
  public required ulong StartTime { get; init; }
  public required ulong EndTime { get; init; }
  public required string Profile { get; init; }
  public required long SizeBytes { get; init; }
}
