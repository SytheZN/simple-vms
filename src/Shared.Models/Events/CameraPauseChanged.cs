namespace Shared.Models.Events;

public sealed class CameraPauseChanged : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required ulong? PausedUntil { get; init; }
  public required ulong Timestamp { get; init; }
}
