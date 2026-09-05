namespace Shared.Models.Events;

public sealed class SystemEventRecorded : ISystemEvent
{
  public required Guid Id { get; init; }
  public required string Type { get; init; }
  public required string Source { get; init; }
  public required ulong Timestamp { get; init; }
  public Dictionary<string, string>? Metadata { get; init; }
}
