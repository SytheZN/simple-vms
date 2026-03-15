namespace Shared.Models.Events;

public sealed class MotionDetected : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required ulong Timestamp { get; init; }
}
