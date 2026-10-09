using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Api;
using Shared.Models;
using Shared.Models.Events;

namespace Server.Core.Services;

public sealed class CameraPauseService : ICameraPauseState, IAsyncDisposable
{
  private const string ConfigNamespace = "server";
  private const string KeyPrefix = "camera/";
  private const string KeySuffix = "/pausedUntil";

  private readonly IPluginHost _plugins;
  private readonly CameraStatusTracker _status;
  private readonly IEventBus _eventBus;
  private readonly TimeProvider _time;
  private readonly ILogger<CameraPauseService> _logger;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly ConcurrentDictionary<Guid, Pause> _pauses = new();
  private CancellationTokenSource? _eventCts;

  private sealed record Pause(ulong Until, ITimer Expiry);

  public CameraPauseService(
    IPluginHost plugins, CameraStatusTracker status, IEventBus eventBus,
    TimeProvider time, ILogger<CameraPauseService> logger)
  {
    _plugins = plugins;
    _status = status;
    _eventBus = eventBus;
    _time = time;
    _logger = logger;
  }

  public bool IsPaused(Guid cameraId) => _pauses.ContainsKey(cameraId);

  public ulong? GetPausedUntil(Guid cameraId) =>
    _pauses.TryGetValue(cameraId, out var pause) ? pause.Until : null;

  public async Task StartAsync(CancellationToken ct)
  {
    var config = _plugins.DataProvider.Config;
    var stored = await config.GetAllAsync(ConfigNamespace, ct);
    if (stored.IsT1)
    {
      _logger.LogError("Failed to load camera pauses: {Message}", stored.AsT1.Message);
    }
    else
    {
      foreach (var (key, value) in stored.AsT0)
      {
        if (!TryParseCameraId(key, out var cameraId) || !ulong.TryParse(value, out var until))
          continue;

        if (until > NowMicros())
          Arm(cameraId, until);
        else
          await config.DeleteAsync(ConfigNamespace, key, ct);
      }
    }

    _eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    WatchCameraRemoved(_eventCts.Token);
  }

  public async Task<OneOf<PauseCameraResponse, Error>> PauseAsync(
    Guid cameraId, uint durationSeconds, CancellationToken ct)
  {
    var camera = await _plugins.DataProvider.Cameras.GetByIdAsync(cameraId, ct);
    if (camera.IsT1) return camera.AsT1;

    ulong? until = durationSeconds == 0 ? null : NowMicros() + durationSeconds * 1_000_000UL;
    bool changed;

    await _gate.WaitAsync(ct);
    try
    {
      var config = _plugins.DataProvider.Config;
      var persisted = until is { } value
        ? await config.SetAsync(ConfigNamespace, Key(cameraId), value.ToString(), ct)
        : await config.DeleteAsync(ConfigNamespace, Key(cameraId), ct);
      if (persisted.IsT1) return persisted.AsT1;

      if (until is { } armAt)
      {
        Arm(cameraId, armAt);
        changed = true;
      }
      else
      {
        changed = Disarm(cameraId);
      }
    }
    finally
    {
      _gate.Release();
    }

    if (changed)
      await PublishAsync(cameraId, until, ct);

    return new PauseCameraResponse { PausedUntil = until };
  }

  private async Task ExpireAsync(Guid cameraId, ulong until)
  {
    try
    {
      await _gate.WaitAsync();
      try
      {
        if (!_pauses.TryGetValue(cameraId, out var pause) || pause.Until != until)
          return;
        await _plugins.DataProvider.Config.DeleteAsync(ConfigNamespace, Key(cameraId));
        Disarm(cameraId);
      }
      finally
      {
        _gate.Release();
      }

      await PublishAsync(cameraId, null, CancellationToken.None);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to resume camera {CameraId} after pause expiry", cameraId);
    }
  }

  private void Arm(Guid cameraId, ulong until)
  {
    var dueTime = TimeSpan.FromMicroseconds((long)(until - NowMicros()));
    var expiry = _time.CreateTimer(
      _ => _ = ExpireAsync(cameraId, until), null, dueTime, Timeout.InfiniteTimeSpan);

    if (_pauses.TryGetValue(cameraId, out var previous))
      previous.Expiry.Dispose();
    _pauses[cameraId] = new Pause(until, expiry);
    _status.SetPausedUntil(cameraId, until);
  }

  private bool Disarm(Guid cameraId)
  {
    if (!_pauses.TryRemove(cameraId, out var pause))
      return false;
    pause.Expiry.Dispose();
    _status.SetPausedUntil(cameraId, null);
    return true;
  }

  private Task PublishAsync(Guid cameraId, ulong? until, CancellationToken ct) =>
    _eventBus.PublishAsync(new CameraPauseChanged
    {
      CameraId = cameraId,
      PausedUntil = until,
      Timestamp = NowMicros()
    }, ct);

  private void WatchCameraRemoved(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraRemoved>(ct))
      {
        try
        {
          await _gate.WaitAsync(ct);
          try
          {
            Disarm(evt.CameraId);
            await _plugins.DataProvider.Config.DeleteAsync(ConfigNamespace, Key(evt.CameraId), ct);
          }
          finally
          {
            _gate.Release();
          }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to clear pause for removed camera {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  private ulong NowMicros() => _time.GetUtcNow().ToUnixMicroseconds();

  private static string Key(Guid cameraId) => $"{KeyPrefix}{cameraId}{KeySuffix}";

  private static bool TryParseCameraId(string key, out Guid cameraId)
  {
    cameraId = Guid.Empty;
    return key.StartsWith(KeyPrefix, StringComparison.Ordinal)
      && key.EndsWith(KeySuffix, StringComparison.Ordinal)
      && Guid.TryParse(key.AsSpan(KeyPrefix.Length, key.Length - KeyPrefix.Length - KeySuffix.Length), out cameraId);
  }

  public ValueTask DisposeAsync()
  {
    _eventCts?.Cancel();
    _eventCts?.Dispose();
    foreach (var pause in _pauses.Values)
      pause.Expiry.Dispose();
    _pauses.Clear();
    _gate.Dispose();
    return ValueTask.CompletedTask;
  }
}
