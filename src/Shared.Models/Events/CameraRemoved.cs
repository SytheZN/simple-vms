namespace Shared.Models.Events;

public sealed class CameraRemoved : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required ulong Timestamp { get; init; }
}
