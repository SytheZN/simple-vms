namespace Shared.Models.Events;

public sealed class MotionEnded : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required ulong Timestamp { get; init; }
}
