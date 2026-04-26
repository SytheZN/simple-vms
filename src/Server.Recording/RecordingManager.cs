using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Server.Streaming;
using Shared.Models;
using Shared.Models.Events;

namespace Server.Recording;

public sealed class RecordingManager : IAsyncDisposable
{
  private const int DefaultSegmentDurationSeconds = 300;

  private readonly IPluginHost _plugins;
  private readonly StreamTapRegistry _tapRegistry;
  private readonly IEventBus _eventBus;
  private readonly ILogger _logger;
  private readonly ConcurrentDictionary<(Guid CameraId, string Profile), (SegmentWriter Writer, CancellationTokenSource Cts)> _writers = new();
  private CancellationTokenSource? _eventCts;
  private IStorageProvider? _defaultStorage;
  private bool _disposed;

  public ByteRateTracker ByteRateTracker { get; } = new();
  internal int WriterCount => _writers.Count;

  public RecordingManager(
    IPluginHost plugins,
    StreamTapRegistry tapRegistry,
    IEventBus eventBus,
    ILogger logger)
  {
    _plugins = plugins;
    _tapRegistry = tapRegistry;
    _eventBus = eventBus;
    _logger = logger;
  }

  public async Task StartAsync(CancellationToken ct)
  {
    var storage = _plugins.StorageProviders.FirstOrDefault();
    if (storage == null)
    {
      _logger.LogWarning("No storage provider available, recording disabled");
      return;
    }

    var defaultDuration = await GetDefaultSegmentDurationAsync(ct);
    var data = _plugins.DataProvider;
    var camerasResult = await data.Cameras.GetAllAsync(ct);
    if (camerasResult.IsT1)
    {
      _logger.LogError("Failed to load cameras for recording: {Message}", camerasResult.AsT1.Message);
      return;
    }

    _defaultStorage = storage;

    foreach (var camera in camerasResult.AsT0)
      await ReconcileAsync(camera.Id, ct);

    _eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    WatchStreamStopped(_eventCts.Token);
    WatchStreamStarted(_eventCts.Token);
    WatchCameraConfigChanged(_eventCts.Token);
    WatchCameraRemoved(_eventCts.Token);

    _logger.LogInformation("Recording manager started: {Count} stream(s)", _writers.Count);
  }

  private void StartWriter(
    Guid cameraId, string profile, string codec, Guid streamId,
    int segmentDuration, IStorageProvider storage, CancellationToken ct)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var writer = new SegmentWriter(
      cameraId, profile, codec, streamId,
      segmentDuration, storage, _plugins.DataProvider, _eventBus, _logger);

    _writers[(cameraId, profile)] = (writer, cts);

    _ = RunWriterAsync(cameraId, profile, codec, streamId,
      segmentDuration, storage, cts.Token);

    _logger.LogInformation(
      "Started recording camera {CameraId} profile '{Profile}' ({Duration}s segments)",
      cameraId, profile, segmentDuration);
  }

  internal async Task StopWriterAsync(Guid cameraId, string profile)
  {
    if (_writers.TryRemove((cameraId, profile), out var entry))
    {
      entry.Cts.Cancel();
      await entry.Writer.DisposeAsync();
      entry.Cts.Dispose();
      _logger.LogInformation(
        "Stopped recording camera {CameraId} profile '{Profile}'",
        cameraId, profile);
    }
  }

  internal async Task ReconcileAsync(Guid cameraId, CancellationToken ct)
  {
    var storage = _defaultStorage ?? _plugins.StorageProviders.FirstOrDefault();
    if (storage == null) return;

    var data = _plugins.DataProvider;
    var streamsResult = await data.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return;

    var cameraResult = await data.Cameras.GetByIdAsync(cameraId, ct);
    if (cameraResult.IsT1) return;
    var camera = cameraResult.AsT0;

    var defaultDuration = await GetDefaultSegmentDurationAsync(ct);
    var desiredProfiles = new HashSet<string>();

    foreach (var stream in streamsResult.AsT0)
    {
      if (!stream.RecordingEnabled)
        continue;

      desiredProfiles.Add(stream.Profile);

      if (_writers.ContainsKey((cameraId, stream.Profile)))
        continue;

      var duration = camera.SegmentDuration ?? defaultDuration;
      StartWriter(cameraId, stream.Profile, stream.Codec ?? "unknown",
        stream.Id, duration, storage, ct);
    }

    var toStop = _writers.Keys
      .Where(k => k.CameraId == cameraId && !desiredProfiles.Contains(k.Profile))
      .ToList();
    foreach (var key in toStop)
      await StopWriterAsync(key.CameraId, key.Profile);
  }

  private void WatchStreamStopped(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<StreamStopped>(ct))
      {
        if (_writers.TryGetValue((evt.CameraId, evt.Profile), out var entry))
        {
          _logger.LogDebug(
            "Stream stopped for camera {CameraId} profile '{Profile}', sealing segment",
            evt.CameraId, evt.Profile);
          try
          {
            entry.Writer.Seal();
          }
          catch (Exception ex)
          {
            _logger.LogError(ex,
              "Failed to seal segment on disconnect for camera {CameraId} profile '{Profile}'",
              evt.CameraId, evt.Profile);
          }
        }
      }
    }, ct);
  }

  private void WatchStreamStarted(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<StreamStarted>(ct))
      {
        try
        {
          await ReconcileAsync(evt.CameraId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to reconcile on StreamStarted for camera {CameraId}", evt.CameraId);
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
          await ReconcileAsync(evt.CameraId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to reconcile on CameraConfigChanged for camera {CameraId}", evt.CameraId);
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
        var toStop = _writers.Keys
          .Where(k => k.CameraId == evt.CameraId)
          .ToList();
        foreach (var key in toStop)
        {
          try
          {
            await StopWriterAsync(key.CameraId, key.Profile);
          }
          catch (Exception ex)
          {
            _logger.LogError(ex,
              "Failed to stop writer on CameraRemoved for camera {CameraId} profile '{Profile}'",
              key.CameraId, key.Profile);
          }
        }
      }
    }, ct);
  }

  private const int MaxConsecutiveFailures = 5;
  private static readonly int[] BackoffSeconds = [1, 2, 4, 8, 15];

  private async Task RunWriterAsync(
    Guid cameraId, string profile, string codec, Guid streamId,
    int segmentDuration, IStorageProvider storage, CancellationToken ct)
  {
    var consecutiveFailures = 0;

    while (!ct.IsCancellationRequested)
    {
      var pipeline = _tapRegistry.GetPipeline(cameraId, profile);
      if (pipeline == null || !pipeline.IsConstructed)
      {
        consecutiveFailures++;
        if (consecutiveFailures >= MaxConsecutiveFailures)
        {
          _logger.LogError(
            "Giving up recording camera {CameraId} profile '{Profile}' after {Count} failures (no pipeline)",
            cameraId, profile, consecutiveFailures);
          return;
        }
        try { await DelayBackoff(consecutiveFailures, ct); }
        catch (OperationCanceledException) { return; }
        continue;
      }

      var header = pipeline.MuxHeader;
      var muxResult = await _tapRegistry.SubscribeMuxAsync(cameraId, profile, ct);
      if (muxResult.IsT1)
      {
        consecutiveFailures++;
        if (consecutiveFailures >= MaxConsecutiveFailures)
        {
          _logger.LogError(
            "Giving up recording camera {CameraId} profile '{Profile}' after {Count} failures (subscribe failed)",
            cameraId, profile, consecutiveFailures);
          return;
        }
        try { await DelayBackoff(consecutiveFailures, ct); }
        catch (OperationCanceledException) { return; }
        continue;
      }

      if (!_writers.TryGetValue((cameraId, profile), out var entry))
        return;

      entry.Writer.OnSegmentFinalized = (sid, bytes, start, end) =>
      {
        consecutiveFailures = 0;
        ByteRateTracker.Record(sid, bytes, start, end);
      };

      try
      {
        await entry.Writer.RunAsync(muxResult.AsT0, header, ct);
        return;
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        consecutiveFailures++;
        _logger.LogError(ex,
          "Recording writer failed for camera {CameraId} profile '{Profile}' (failure {Count}/{Max})",
          cameraId, profile, consecutiveFailures, MaxConsecutiveFailures);

        if (consecutiveFailures >= MaxConsecutiveFailures)
        {
          _logger.LogError(
            "Giving up recording camera {CameraId} profile '{Profile}' after {Count} consecutive failures",
            cameraId, profile, consecutiveFailures);
          return;
        }

        try { await DelayBackoff(consecutiveFailures, ct); }
        catch (OperationCanceledException) { return; }

        var newWriter = new SegmentWriter(
          cameraId, profile, codec, streamId,
          segmentDuration, storage, _plugins.DataProvider, _eventBus, _logger);
        if (_writers.TryGetValue((cameraId, profile), out var current))
        {
          try
          {
            using var disposeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await current.Writer.DisposeAsync().AsTask().WaitAsync(disposeCts.Token);
          }
          catch { }
          _writers[(cameraId, profile)] = (newWriter, current.Cts);
        }
        else
        {
          return;
        }
      }
    }
  }

  private static Task DelayBackoff(int failureCount, CancellationToken ct)
  {
    var idx = Math.Min(failureCount, BackoffSeconds.Length - 1);
    return Task.Delay(TimeSpan.FromSeconds(BackoffSeconds[idx]), ct);
  }

  private async Task<int> GetDefaultSegmentDurationAsync(CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Config.GetAsync("server", "server.segmentDuration", ct);
    if (result.IsT0 && int.TryParse(result.AsT0, out var duration) && duration > 0)
      return duration;
    return DefaultSegmentDurationSeconds;
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    _eventCts?.Cancel();
    _eventCts?.Dispose();

    foreach (var (_, (writer, cts)) in _writers)
    {
      cts.Cancel();
      await writer.DisposeAsync();
      cts.Dispose();
    }
    _writers.Clear();

    _logger.LogInformation("Recording manager stopped");
  }
}
