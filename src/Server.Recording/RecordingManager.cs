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
  private readonly Dictionary<(Guid CameraId, string Profile), (SegmentWriter Writer, CancellationTokenSource Cts)> _writers = [];
  private CancellationTokenSource? _eventCts;
  private bool _disposed;

  public ByteRateTracker ByteRateTracker { get; } = new();

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

    foreach (var camera in camerasResult.AsT0)
    {
      var streamsResult = await data.Streams.GetByCameraIdAsync(camera.Id, ct);
      if (streamsResult.IsT1)
        continue;

      foreach (var stream in streamsResult.AsT0)
      {
        if (!stream.RecordingEnabled)
          continue;

        var pipeline = _tapRegistry.GetPipeline(camera.Id, stream.Profile);
        if (pipeline == null || !pipeline.IsConstructed)
        {
          _logger.LogWarning(
            "Cannot record camera {CameraId} profile '{Profile}': pipeline not available",
            camera.Id, stream.Profile);
          continue;
        }

        var header = pipeline.VideoHeader;
        var videoResult = await _tapRegistry.SubscribeVideoAsync(camera.Id, stream.Profile, ct);
        if (videoResult.IsT1)
        {
          _logger.LogWarning(
            "Cannot record camera {CameraId} profile '{Profile}': {Message}",
            camera.Id, stream.Profile, videoResult.AsT1.Message);
          continue;
        }

        var duration = camera.SegmentDuration ?? defaultDuration;
        var writer = new SegmentWriter(
          camera.Id, stream.Profile, stream.Codec ?? "unknown", stream.Id,
          duration, storage, data, _eventBus, _logger);
        writer.OnSegmentFinalized = (streamId, bytes, start, end) =>
          ByteRateTracker.Record(streamId, bytes, start, end);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _writers[(camera.Id, stream.Profile)] = (writer, cts);

        _ = RunWriterAsync(writer, videoResult.AsT0, header, cts.Token);

        _logger.LogInformation(
          "Started recording camera {CameraId} profile '{Profile}' ({Duration}s segments)",
          camera.Id, stream.Profile, duration);
      }
    }

    _eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    WatchStreamStopped(_eventCts.Token);

    _logger.LogInformation("Recording manager started: {Count} stream(s)", _writers.Count);
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

  private async Task RunWriterAsync(
    SegmentWriter writer, IVideoStream videoStream, ReadOnlyMemory<byte> header, CancellationToken ct)
  {
    try
    {
      await writer.RunAsync(videoStream, header, ct);
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Recording writer failed");
    }
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
