using System.Collections.Concurrent;
using Shared.Models;

namespace Server.Core;

public sealed class CameraStatusTracker
{
  private readonly ConcurrentDictionary<(Guid CameraId, string Profile), string> _pipelines = new();
  private readonly ConcurrentDictionary<(Guid CameraId, string Profile), RecordingState> _writers = new();
  private readonly ConcurrentDictionary<Guid, ulong> _pausedUntil = new();

  public string GetStatus(Guid cameraId)
  {
    if (_pausedUntil.ContainsKey(cameraId)) return "paused";

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

  public ulong? GetPausedUntil(Guid cameraId) =>
    _pausedUntil.TryGetValue(cameraId, out var until) ? until : null;

  public void SetPausedUntil(Guid cameraId, ulong? until)
  {
    if (until is { } value)
      _pausedUntil[cameraId] = value;
    else
      _pausedUntil.TryRemove(cameraId, out _);
  }

  public void Remove(Guid cameraId)
  {
    _pausedUntil.TryRemove(cameraId, out _);
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
