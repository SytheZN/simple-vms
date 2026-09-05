using System.Collections.Concurrent;
using Shared.Models;

namespace Server.Core;

public sealed class CameraStatusTracker
{
  private readonly ConcurrentDictionary<(Guid CameraId, string Profile), string> _pipelines = new();
  private readonly ConcurrentDictionary<(Guid CameraId, string Profile), RecordingState> _writers = new();

  public string GetStatus(Guid cameraId)
  {
    var anyOnline = false;
    foreach (var kvp in _pipelines)
    {
      if (kvp.Key.CameraId == cameraId && kvp.Value == "online")
      {
        anyOnline = true;
        break;
      }
    }
    if (!anyOnline) return "offline";

    var recording = false;
    foreach (var kvp in _writers)
    {
      if (kvp.Key.CameraId != cameraId) continue;
      if (kvp.Value == RecordingState.Error) return "error";
      if (kvp.Value == RecordingState.Active) recording = true;
    }
    return recording ? "recording" : "online";
  }

  public void SetStatus(Guid cameraId, string profile, string status) =>
    _pipelines[(cameraId, profile)] = status;

  public void SetRecording(Guid cameraId, string profile, RecordingState state)
  {
    if (state == RecordingState.None)
      _writers.TryRemove((cameraId, profile), out _);
    else
      _writers[(cameraId, profile)] = state;
  }

  public void Remove(Guid cameraId)
  {
    foreach (var key in _pipelines.Keys)
    {
      if (key.CameraId == cameraId)
        _pipelines.TryRemove(key, out _);
    }
    foreach (var key in _writers.Keys)
    {
      if (key.CameraId == cameraId)
        _writers.TryRemove(key, out _);
    }
  }
}
