namespace Shared.Models.Events;

public sealed class CameraAdded : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required ulong Timestamp { get; init; }
}
