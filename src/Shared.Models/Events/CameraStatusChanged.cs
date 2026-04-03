namespace Shared.Models.Events;

public sealed class CameraStatusChanged : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required string Profile { get; init; }
  public required string Status { get; init; }
  public string? Reason { get; init; }
  public required ulong Timestamp { get; init; }
}
