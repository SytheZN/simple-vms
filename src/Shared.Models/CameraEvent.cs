namespace Shared.Models;

public sealed class CameraEvent
{
  public required Guid Id { get; set; }
  public required Guid CameraId { get; set; }
  public required string Type { get; set; }
  public required ulong StartTime { get; set; }
  public ulong? EndTime { get; set; }
  public Dictionary<string, string>? Metadata { get; set; }
}
