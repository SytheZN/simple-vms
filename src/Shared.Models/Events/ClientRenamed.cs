namespace Shared.Models.Events;

public sealed class ClientRenamed : ISystemEvent
{
  public required Guid ClientId { get; init; }
  public required string PreviousName { get; init; }
  public required string Name { get; init; }
  public required ulong Timestamp { get; init; }
}
