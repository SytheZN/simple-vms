using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Entities;
using Shared.Models.Events;

namespace Server.Core;

public sealed class EventManager : IAsyncDisposable
{
  private const int MaxConsecutiveFailures = 5;
  private const int MaxOpenEventsPerCamera = 64;
  private static readonly int[] BackoffSeconds = [1, 2, 4, 8, 15];

  private readonly IPluginHost _plugins;
  private readonly IEventBus _eventBus;
  private readonly ILogger _logger;
  private readonly ConcurrentDictionary<Guid, (IEventSubscription Subscription, CancellationTokenSource Cts)> _subscriptions = new();
  private readonly ConcurrentDictionary<(Guid CameraId, string Profile), bool> _disconnected = new();
  private readonly ConcurrentDictionary<(Guid CameraId, string Type, string Source), CameraEvent> _openEvents = new();
  private CancellationTokenSource? _eventCts;
  private bool _disposed;

  public EventManager(IPluginHost plugins, IEventBus eventBus, ILogger logger)
  {
    _plugins = plugins;
    _eventBus = eventBus;
    _logger = logger;
  }

  internal int SubscriptionCount => _subscriptions.Count;

  public async Task StartAsync(CancellationToken ct)
  {
    var data = _plugins.DataProvider;
    var camerasResult = await data.Cameras.GetAllAsync(ct);
    if (camerasResult.IsT1)
    {
      _logger.LogError("Failed to load cameras for event subscriptions: {Message}",
        camerasResult.AsT1.Message);
      return;
    }

    foreach (var camera in camerasResult.AsT0)
      await ReconcileAsync(camera.Id, ct);

    _eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    WatchCameraAdded(_eventCts.Token);
    WatchCameraUpdated(_eventCts.Token);
    WatchCameraRemoved(_eventCts.Token);
    WatchCameraConfigChanged(_eventCts.Token);
    WatchCameraStatusChanged(_eventCts.Token);
    WatchCameraRecordingChanged(_eventCts.Token);
    WatchClientEvents(_eventCts.Token);

    _logger.LogInformation("Event manager started: {Count} subscription(s)", _subscriptions.Count);
  }

  internal async Task ReconcileAsync(Guid cameraId, CancellationToken ct)
  {
    var data = _plugins.DataProvider;
    var cameraResult = await data.Cameras.GetByIdAsync(cameraId, ct);
    if (cameraResult.IsT1)
    {
      await StopSubscriptionAsync(cameraId);
      return;
    }

    var camera = cameraResult.AsT0;
    if (!camera.Capabilities.Contains("events"))
    {
      await StopSubscriptionAsync(cameraId);
      return;
    }

    if (_subscriptions.ContainsKey(cameraId))
      return;

    StartSubscription(camera, ct);
  }

  private void StartSubscription(Camera camera, CancellationToken ct)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _subscriptions[camera.Id] = (null!, cts);
    _ = RunSubscriptionAsync(camera, cts);
  }

  private async Task RunSubscriptionAsync(Camera camera, CancellationTokenSource cts)
  {
    var ct = cts.Token;
    var consecutiveFailures = 0;

    try
    {
      while (!ct.IsCancellationRequested)
      {
        var config = BuildConfiguration(camera);
        var provider = _plugins.CameraProviders
          .FirstOrDefault(p => p.ProviderId == camera.ProviderId)
          ?? _plugins.CameraProviders.FirstOrDefault();

        if (provider == null)
        {
          _logger.LogWarning("No camera provider for events on camera {CameraId}", camera.Id);
          return;
        }

        IEventSubscription? subscription;
        try
        {
          subscription = await provider.SubscribeEventsAsync(config, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          consecutiveFailures++;
          _logger.LogWarning(ex,
            "Failed to subscribe to events for camera {CameraId} (failure {Count}/{Max})",
            camera.Id, consecutiveFailures, MaxConsecutiveFailures);

          if (consecutiveFailures >= MaxConsecutiveFailures)
          {
            _logger.LogError("Giving up event subscription for camera {CameraId}", camera.Id);
            return;
          }
          await DelayBackoff(consecutiveFailures, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
          if (ct.IsCancellationRequested) return;
          continue;
        }

        if (subscription == null)
        {
          _logger.LogDebug("No event subscription available for camera {CameraId}", camera.Id);
          return;
        }

        _subscriptions[camera.Id] = (subscription, cts);
        _logger.LogInformation("Started event subscription for camera {CameraId}", camera.Id);

        try
        {
          await foreach (var rawEvent in subscription.ReadEventsAsync(ct))
          {
            consecutiveFailures = 0;
            await ProcessEventAsync(rawEvent, ct);
          }

          _logger.LogDebug("Event subscription ended for camera {CameraId}", camera.Id);
          return;
        }
        catch (OperationCanceledException)
        {
          return;
        }
        catch (Exception ex)
        {
          consecutiveFailures++;
          _logger.LogWarning(ex,
            "Event subscription failed for camera {CameraId} (failure {Count}/{Max})",
            camera.Id, consecutiveFailures, MaxConsecutiveFailures);

          _subscriptions.TryRemove(camera.Id, out _);
          await subscription.DisposeAsync();

          if (consecutiveFailures >= MaxConsecutiveFailures)
          {
            _logger.LogError("Giving up event subscription for camera {CameraId}", camera.Id);
            return;
          }

          await DelayBackoff(consecutiveFailures, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
          if (ct.IsCancellationRequested) return;
        }
      }
    }
    finally
    {
      _subscriptions.TryRemove(camera.Id, out _);
      cts.Dispose();
    }
  }

  internal async Task ProcessEventAsync(CameraEvent rawEvent, CancellationToken ct)
  {
    var evt = rawEvent;

    foreach (var filter in _plugins.EventFilters)
    {
      try
      {
        var result = await filter.ProcessAsync(evt, ct);
        if (result.Decision == EventDecision.Suppress)
          return;
        if (result.ModifiedEvent != null)
          evt = result.ModifiedEvent;
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Event filter '{FilterId}' failed", filter.FilterId);
      }
    }

    await RecordStateChangeAsync(evt, ct);

    foreach (var sink in _plugins.NotificationSinks)
    {
      try
      {
        await sink.SendAsync(evt, ct);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Notification sink '{SinkId}' failed", sink.SinkId);
      }
    }

    await _eventBus.PublishAsync(new OnvifEvent
    {
      CameraId = evt.CameraId,
      Topic = evt.Metadata?.GetValueOrDefault("topic") ?? evt.Type,
      Data = evt.Metadata,
      Timestamp = evt.StartTime
    }, ct);

    if (evt.Type == "motion")
    {
      var isActive = ActiveState(evt);
      if (isActive == true)
      {
        await _eventBus.PublishAsync(new MotionDetected
        {
          CameraId = evt.CameraId,
          Timestamp = evt.StartTime
        }, ct);
      }
      else if (isActive == false)
      {
        await _eventBus.PublishAsync(new MotionEnded
        {
          CameraId = evt.CameraId,
          Timestamp = evt.StartTime
        }, ct);
      }
    }
  }

  private async Task RecordStateChangeAsync(CameraEvent evt, CancellationToken ct)
  {
    var active = ActiveState(evt);
    var key = OpenKey(evt);

    if (active == false)
    {
      if (!_openEvents.TryRemove(key, out var started))
      {
        _logger.LogDebug(
          "Ignoring inactive '{Type}' report for camera {CameraId} with nothing open",
          evt.Type, evt.CameraId);
        return;
      }

      started.EndTime = evt.StartTime;
      var updateResult = await _plugins.DataProvider.Events.UpdateAsync(started, ct);
      if (updateResult.IsT1)
        _logger.LogWarning("Failed to close event: {Message}", updateResult.AsT1.Message);
      else
        await PublishRecordedAsync(started, true, ct);
      return;
    }

    if (active == true && _openEvents.ContainsKey(key))
      return;

    var createResult = await _plugins.DataProvider.Events.CreateAsync(evt, ct);
    if (createResult.IsT1)
    {
      _logger.LogWarning("Failed to persist event: {Message}", createResult.AsT1.Message);
      return;
    }

    if (active == true)
      TrackOpen(evt);

    await PublishRecordedAsync(evt, false, ct);
  }

  private void TrackOpen(CameraEvent evt)
  {
    _openEvents[OpenKey(evt)] = evt;

    var forCamera = _openEvents
      .Where(entry => entry.Key.CameraId == evt.CameraId)
      .ToList();
    if (forCamera.Count <= MaxOpenEventsPerCamera)
      return;

    foreach (var stale in forCamera
      .OrderBy(entry => entry.Value.StartTime)
      .Take(forCamera.Count - MaxOpenEventsPerCamera))
    {
      if (_openEvents.TryRemove(stale.Key, out _))
        _logger.LogWarning(
          "Camera {CameraId} has more than {Max} unclosed '{Type}' events; abandoning oldest",
          evt.CameraId, MaxOpenEventsPerCamera, stale.Key.Type);
    }
  }

  private static bool? ActiveState(CameraEvent evt)
  {
    var state = evt.Metadata?.GetValueOrDefault("State")
      ?? evt.Metadata?.GetValueOrDefault("IsMotion");
    if (state == null) return null;
    if (string.Equals(state, "true", StringComparison.OrdinalIgnoreCase)) return true;
    if (string.Equals(state, "false", StringComparison.OrdinalIgnoreCase)) return false;
    return null;
  }

  private static (Guid, string, string) OpenKey(CameraEvent evt)
  {
    var source = evt.Metadata == null
      ? string.Empty
      : string.Join('|', evt.Metadata
        .Where(pair => pair.Key.StartsWith("source.", StringComparison.Ordinal))
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => $"{pair.Key}={pair.Value}"));

    return (evt.CameraId, evt.Type, source);
  }

  private Task PublishRecordedAsync(CameraEvent evt, bool ended, CancellationToken ct) =>
    _eventBus.PublishAsync(new CameraEventRecorded
    {
      Id = evt.Id,
      CameraId = evt.CameraId,
      Type = evt.Type,
      Timestamp = evt.StartTime,
      EndTime = evt.EndTime,
      Metadata = evt.Metadata,
      Ended = ended
    }, ct);

  private async Task StopSubscriptionAsync(Guid cameraId)
  {
    if (_subscriptions.TryRemove(cameraId, out var entry))
    {
      entry.Cts.Cancel();
      if (entry.Subscription != null!)
        await entry.Subscription.DisposeAsync();
      _logger.LogInformation("Stopped event subscription for camera {CameraId}", cameraId);
    }
  }

  private void WatchCameraAdded(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraAdded>(ct))
      {
        try
        {
          var camera = await GetCameraAsync(evt.CameraId, ct);
          await RecordSystemAsync(SystemEventFactory.CameraAdded(
            evt.CameraId, camera?.Name ?? "", camera?.Address ?? "", evt.Timestamp), ct);
          await ReconcileAsync(evt.CameraId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to reconcile events on CameraAdded for {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  private void WatchCameraUpdated(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraUpdated>(ct))
      {
        try
        {
          await RecordSystemAsync(SystemEventFactory.CameraUpdated(
            evt.CameraId, evt.Name, evt.PreviousName, evt.Address,
            evt.ProviderId, evt.CredentialsUpdated, evt.RtspPortOverride, evt.Timestamp), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to record camera update for {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  private void WatchCameraRemoved(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraRemoved>(ct))
      {
        try
        {
          await RecordSystemAsync(SystemEventFactory.CameraRemoved(
            evt.CameraId, evt.Name, evt.Timestamp), ct);
          await StopSubscriptionAsync(evt.CameraId);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Failed to stop events on CameraRemoved for {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  private void WatchCameraConfigChanged(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraConfigChanged>(ct))
      {
        try
        {
          var camera = await GetCameraAsync(evt.CameraId, ct);
          var profiles = await LoadStreamProfilesAsync(evt.CameraId, ct);
          await RecordSystemAsync(SystemEventFactory.CameraReconfigured(
            evt.CameraId, camera?.Name ?? "", evt.Diff, profiles, evt.Timestamp), ct);
          await StopSubscriptionAsync(evt.CameraId);
          await ReconcileAsync(evt.CameraId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to reconcile events on CameraConfigChanged for {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  private void WatchCameraStatusChanged(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraStatusChanged>(ct))
      {
        try
        {
          var key = (evt.CameraId, evt.Profile);
          var metadata = new Dictionary<string, string> { ["profile"] = evt.Profile };

          if (evt is { Status: "offline", Reason: "disconnected" })
          {
            if (_disconnected.TryAdd(key, true))
              await RecordAsync(evt.CameraId, "camera-disconnect", evt.Timestamp, metadata, ct);
          }
          else if (evt.Status == "online" && _disconnected.TryRemove(key, out _))
          {
            await RecordAsync(evt.CameraId, "camera-connect", evt.Timestamp, metadata, ct);
          }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to record status change for {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  private void WatchCameraRecordingChanged(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraRecordingChanged>(ct))
      {
        try
        {
          var metadata = new Dictionary<string, string> { ["profile"] = evt.Profile };
          var type = evt.State switch
          {
            RecordingState.Active => "camera-recording-started",
            RecordingState.Error => "camera-recording-error",
            RecordingState.None => "camera-recording-stopped",
            _ => null
          };
          if (type != null)
            await RecordAsync(evt.CameraId, type, evt.Timestamp, metadata, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to record recording change for {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  private void WatchClientEvents(CancellationToken ct)
  {
    WatchClient<ClientConnected>(ct, async evt => SystemEventFactory.ClientConnected(
      evt.ClientId, await GetClientNameAsync(evt.ClientId, ct), evt.RemoteAddress, evt.Timestamp));
    WatchClient<ClientDisconnected>(ct, async evt => SystemEventFactory.ClientDisconnected(
      evt.ClientId, await GetClientNameAsync(evt.ClientId, ct), evt.Timestamp));
    WatchClient<ClientEnrolled>(ct, evt => Task.FromResult(SystemEventFactory.ClientEnrolled(
      evt.ClientId, evt.Name, evt.Timestamp)));
    WatchClient<ClientRevoked>(ct, evt => Task.FromResult(SystemEventFactory.ClientRevoked(
      evt.ClientId, evt.Name, evt.Timestamp)));
    WatchClient<ClientRenamed>(ct, evt => Task.FromResult(SystemEventFactory.ClientRenamed(
      evt.ClientId, evt.Name, evt.PreviousName, evt.Timestamp)));
  }

  private void WatchClient<T>(
    CancellationToken ct, Func<T, Task<SystemEvent>> project) where T : ISystemEvent
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<T>(ct))
      {
        try
        {
          await RecordSystemAsync(await project(evt), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to record {EventType} system event", typeof(T).Name);
        }
      }
    }, ct);
  }

  private async Task<string> GetClientNameAsync(Guid clientId, CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Clients.GetByIdAsync(clientId, ct);
    return result.IsT0 ? result.AsT0.Name : "";
  }

  private async Task<IReadOnlyDictionary<Guid, string>> LoadStreamProfilesAsync(
    Guid cameraId, CancellationToken ct)
  {
    try
    {
      var streamsResult = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
      return streamsResult.IsT0
        ? streamsResult.AsT0.ToDictionary(s => s.Id, s => s.Profile)
        : new Dictionary<Guid, string>();
    }
    catch
    {
      return new Dictionary<Guid, string>();
    }
  }

  private async Task<Camera?> GetCameraAsync(Guid cameraId, CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Cameras.GetByIdAsync(cameraId, ct);
    return result.IsT0 ? result.AsT0 : null;
  }

  private async Task RecordSystemAsync(SystemEvent evt, CancellationToken ct)
  {
    var result = await _plugins.DataProvider.SystemEvents.CreateAsync(evt, ct);
    if (result.IsT1)
    {
      _logger.LogWarning("Failed to persist '{Type}' system event: {Message}",
        evt.Type, result.AsT1.Message);
      return;
    }

    await _eventBus.PublishAsync(new SystemEventRecorded
    {
      Id = evt.Id,
      Type = evt.Type,
      Source = evt.Source,
      Timestamp = evt.Timestamp,
      Metadata = evt.Metadata
    }, ct);
  }

  private async Task RecordAsync(
    Guid cameraId, string type, ulong timestamp,
    Dictionary<string, string>? metadata, CancellationToken ct)
  {
    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = cameraId,
      Type = type,
      StartTime = timestamp,
      Metadata = metadata
    };

    var result = await _plugins.DataProvider.Events.CreateAsync(evt, ct);
    if (result.IsT1)
    {
      _logger.LogWarning("Failed to persist '{Type}' event for camera {CameraId}: {Message}",
        type, cameraId, result.AsT1.Message);
      return;
    }

    await PublishRecordedAsync(evt, false, ct);
  }

  private static CameraConfiguration BuildConfiguration(Camera camera)
  {
    Credentials? creds = null;
    if (camera.Credentials is { Length: > 0 })
    {
      try
      {
        var dict = camera.Credentials.ParseCredentials();
        if (dict != null)
          creds = Credentials.FromUserPass(
            dict.TryGetValue("username", out var u) ? u : "",
            dict.TryGetValue("password", out var p) ? p : "");
      }
      catch { }
    }

    return new CameraConfiguration
    {
      Address = camera.Address,
      Name = camera.Name,
      Streams = [],
      Capabilities = camera.Capabilities,
      Config = new Dictionary<string, string>(camera.Config)
      {
        ["cameraId"] = camera.Id.ToString()
      },
      Credentials = creds
    };
  }

  private static Task DelayBackoff(int failureCount, CancellationToken ct)
  {
    var idx = Math.Min(failureCount, BackoffSeconds.Length - 1);
    return Task.Delay(TimeSpan.FromSeconds(BackoffSeconds[idx]), ct);
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    _eventCts?.Cancel();
    _eventCts?.Dispose();

    foreach (var (_, (subscription, cts)) in _subscriptions)
    {
      cts.Cancel();
      if (subscription != null!)
        await subscription.DisposeAsync();
    }
    _subscriptions.Clear();

    _logger.LogInformation("Event manager stopped");
  }
}
