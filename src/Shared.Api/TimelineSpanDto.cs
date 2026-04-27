namespace Shared.Api;

public sealed class TimelineSpanDto
{
  public required ulong StartTime { get; init; }
  public required ulong EndTime { get; init; }
}
