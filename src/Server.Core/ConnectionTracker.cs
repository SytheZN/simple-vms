using System.Collections.Concurrent;

namespace Server.Core;

public sealed class ConnectionTracker
{
  private readonly ConcurrentDictionary<Guid, bool> _connected = new();

  public bool IsConnected(Guid clientId) =>
    _connected.GetValueOrDefault(clientId);

  public void SetConnected(Guid clientId, bool connected) =>
    _connected[clientId] = connected;

  public void Remove(Guid clientId) =>
    _connected.TryRemove(clientId, out _);
}
