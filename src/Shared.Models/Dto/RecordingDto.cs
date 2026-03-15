namespace Shared.Models.Dto;

public sealed class RecordingSegmentDto
{
  public required Guid Id { get; init; }
  public required ulong StartTime { get; init; }
  public required ulong EndTime { get; init; }
  public required string Profile { get; init; }
  public required long SizeBytes { get; init; }
}

public sealed class TimelineResponse
{
  public required IReadOnlyList<TimelineSpan> Spans { get; init; }
  public required IReadOnlyList<TimelineEvent> Events { get; init; }
}

public sealed class TimelineSpan
{
  public required ulong StartTime { get; init; }
  public required ulong EndTime { get; init; }
}

public sealed class TimelineEvent
{
  public required Guid Id { get; init; }
  public required string Type { get; init; }
  public required ulong StartTime { get; init; }
  public ulong? EndTime { get; init; }
}
