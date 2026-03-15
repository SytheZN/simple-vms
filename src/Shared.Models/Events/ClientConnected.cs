namespace Shared.Models.Events;

public sealed class ClientConnected : ISystemEvent
{
  public required Guid ClientId { get; init; }
  public required string RemoteAddress { get; init; }
  public required ulong Timestamp { get; init; }
}
