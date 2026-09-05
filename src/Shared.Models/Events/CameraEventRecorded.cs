namespace Shared.Models.Events;

public sealed class CameraEventRecorded : ISystemEvent
{
  public required Guid Id { get; init; }
  public required Guid CameraId { get; init; }
  public required string Type { get; init; }
  public required ulong Timestamp { get; init; }
  public ulong? EndTime { get; init; }
  public Dictionary<string, string>? Metadata { get; init; }

  public bool Ended { get; init; }
}
