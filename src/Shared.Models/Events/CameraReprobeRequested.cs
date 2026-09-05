namespace Shared.Models.Events;

public sealed class CameraReprobeRequested : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required string Initiator { get; init; }
  public IReadOnlyList<Guid>? StreamIds { get; init; }
  public required ulong Timestamp { get; init; }
}
