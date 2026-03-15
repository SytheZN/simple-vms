namespace Shared.Models.Dto;

public sealed class EventDto
{
  public required Guid Id { get; init; }
  public required Guid CameraId { get; init; }
  public required string Type { get; init; }
  public required ulong StartTime { get; init; }
  public ulong? EndTime { get; init; }
  public Dictionary<string, string>? Metadata { get; init; }
}
