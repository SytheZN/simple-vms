namespace Shared.Models.Events;

public sealed class RecordingSegmentCompleted : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required string Profile { get; init; }
  public required Guid SegmentId { get; init; }
  public required ulong StartTime { get; init; }
  public required ulong EndTime { get; init; }
  public required long SizeBytes { get; init; }
  public required ulong Timestamp { get; init; }
}
