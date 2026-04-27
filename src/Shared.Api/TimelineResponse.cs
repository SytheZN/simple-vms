namespace Shared.Api;

public sealed class TimelineResponse
{
  public required IReadOnlyList<TimelineSpanDto> Spans { get; init; }
  public required IReadOnlyList<TimelineEventDto> Events { get; init; }
}
