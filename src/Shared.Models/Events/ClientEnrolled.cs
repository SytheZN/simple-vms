namespace Shared.Models.Events;

public sealed class ClientEnrolled : ISystemEvent
{
  public required Guid ClientId { get; init; }
  public required string Name { get; init; }
  public required ulong Timestamp { get; init; }
}
