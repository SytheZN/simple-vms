namespace Shared.Models.Events;

public sealed class StreamStopped : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required string Profile { get; init; }
  public string? Reason { get; init; }
  public required ulong Timestamp { get; init; }
}
