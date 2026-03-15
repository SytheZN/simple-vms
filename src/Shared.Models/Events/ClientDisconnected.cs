namespace Shared.Models.Events;

public sealed class ClientDisconnected : ISystemEvent
{
  public required Guid ClientId { get; init; }
  public string? Reason { get; init; }
  public required ulong Timestamp { get; init; }
}
