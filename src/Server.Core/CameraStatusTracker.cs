using System.Collections.Concurrent;

namespace Server.Core;

public sealed class CameraStatusTracker
{
  private readonly ConcurrentDictionary<Guid, string> _statuses = new();

  public string GetStatus(Guid cameraId) =>
    _statuses.GetValueOrDefault(cameraId, "offline");

  public void SetStatus(Guid cameraId, string status) =>
    _statuses[cameraId] = status;

  public void Remove(Guid cameraId) =>
    _statuses.TryRemove(cameraId, out _);
}
