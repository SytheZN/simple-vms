using System.Collections.Concurrent;

namespace Server.Core;

public sealed class CameraStatusTracker
{
  private readonly ConcurrentDictionary<(Guid CameraId, string Profile), string> _pipelines = new();

  public string GetStatus(Guid cameraId)
  {
    foreach (var kvp in _pipelines)
    {
      if (kvp.Key.CameraId == cameraId && kvp.Value == "online")
        return "online";
    }
    return "offline";
  }

  public void SetStatus(Guid cameraId, string profile, string status) =>
    _pipelines[(cameraId, profile)] = status;

  public void Remove(Guid cameraId)
  {
    foreach (var key in _pipelines.Keys)
    {
      if (key.CameraId == cameraId)
        _pipelines.TryRemove(key, out _);
    }
  }
}
