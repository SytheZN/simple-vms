namespace Shared.Api;

public sealed class TimelineEventDto
{
  public required Guid Id { get; init; }
  public required string Type { get; init; }
  public required ulong StartTime { get; init; }
  public ulong? EndTime { get; init; }
}
