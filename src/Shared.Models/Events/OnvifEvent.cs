namespace Shared.Models.Events;

public sealed class OnvifEvent : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required string Topic { get; init; }
  public Dictionary<string, string>? Data { get; init; }
  public required ulong Timestamp { get; init; }
}
