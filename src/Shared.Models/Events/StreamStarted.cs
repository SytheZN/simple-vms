namespace Shared.Models.Events;

public sealed class StreamStarted : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required string Profile { get; init; }
  public required ulong Timestamp { get; init; }
}
