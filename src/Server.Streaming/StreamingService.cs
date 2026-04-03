using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Events;

namespace Server.Streaming;

public sealed class StreamingService : IAsyncDisposable
{
  private readonly IPluginHost _pluginHost;
  private readonly StreamTapRegistry _tapRegistry;
  private readonly IEventBus _eventBus;
  private readonly ILogger<StreamingService> _logger;
  private CancellationTokenSource? _eventCts;

  public StreamingService(
    IPluginHost pluginHost,
    StreamTapRegistry tapRegistry,
    IEventBus eventBus,
    ILogger<StreamingService> logger)
  {
    _pluginHost = pluginHost;
    _tapRegistry = tapRegistry;
    _eventBus = eventBus;
    _logger = logger;
  }

  public async Task StartAsync(CancellationToken ct)
  {
    var dataProvider = _pluginHost.DataProvider;
    var camerasResult = await dataProvider.Cameras.GetAllAsync(ct);
    if (camerasResult.IsT1)
    {
      _logger.LogError("Failed to load cameras: {Message}", camerasResult.AsT1.Message);
      return;
    }

    foreach (var camera in camerasResult.AsT0)
    {
      var streamsResult = await dataProvider.Streams.GetByCameraIdAsync(camera.Id, ct);
      if (streamsResult.IsT1)
        continue;

      foreach (var stream in streamsResult.AsT0)
      {
        var captureSource = FindCaptureSource(stream.Uri);
        if (captureSource == null)
        {
          _logger.LogWarning("No capture source for stream URI '{Uri}' on camera {CameraId}",
            stream.Uri, camera.Id);
          continue;
        }

        var connectionInfo = BuildConnectionInfo(camera, stream);
        var pipeline = new CameraPipeline(
          camera.Id, stream.Profile, connectionInfo,
          captureSource, _pluginHost,
          _eventBus, _logger);

        pipeline.OnParameterMismatch = () => _ = RebuildPipelineAsync(camera, stream);
        _tapRegistry.RegisterPipeline(pipeline);

        var constructResult = await pipeline.ConstructAsync(ct);
        if (constructResult.IsT1)
        {
          _logger.LogWarning(
            "Failed to construct pipeline for camera {CameraId} profile '{Profile}': {Message}",
            camera.Id, stream.Profile, constructResult.AsT1.Message);
        }
      }
    }

    _eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    WatchCameraAdded(_eventCts.Token);
    WatchCameraRemoved(_eventCts.Token);
    WatchCameraConfigChanged(_eventCts.Token);

    _logger.LogInformation("Streaming service started: {Count} pipeline(s) registered",
      _tapRegistry.Pipelines.Count);
  }

  private void WatchCameraAdded(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraAdded>(ct))
      {
        try { await AddPipelinesForCameraAsync(evt.CameraId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to add pipelines for camera {CameraId}", evt.CameraId);
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
        try { await RemovePipelinesForCameraAsync(evt.CameraId); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to remove pipelines for camera {CameraId}", evt.CameraId);
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
        try { await ReconcilePipelinesForCameraAsync(evt.CameraId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to reconcile pipelines for camera {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  private async Task AddPipelinesForCameraAsync(Guid cameraId, CancellationToken ct)
  {
    var dataProvider = _pluginHost.DataProvider;
    var cameraResult = await dataProvider.Cameras.GetByIdAsync(cameraId, ct);
    if (cameraResult.IsT1) return;

    var camera = cameraResult.AsT0;
    var streamsResult = await dataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return;

    foreach (var stream in streamsResult.AsT0)
    {
      if (_tapRegistry.GetPipeline(cameraId, stream.Profile) != null)
        continue;

      var captureSource = FindCaptureSource(stream.Uri);
      if (captureSource == null) continue;

      var connectionInfo = BuildConnectionInfo(camera, stream);
      var pipeline = new CameraPipeline(
        cameraId, stream.Profile, connectionInfo,
        captureSource, _pluginHost, _eventBus, _logger);
      pipeline.OnParameterMismatch = () => _ = RebuildPipelineAsync(camera, stream);
      _tapRegistry.RegisterPipeline(pipeline);

      var result = await pipeline.ConstructAsync(ct);
      if (result.IsT1)
      {
        _logger.LogWarning("Failed to construct pipeline for camera {CameraId} profile '{Profile}': {Message} (will retry)",
          cameraId, stream.Profile, result.AsT1.Message);
        _ = RetryConstructAsync(pipeline, ct);
      }
      else
      {
        _logger.LogInformation("Added pipeline for camera {CameraId} profile '{Profile}'",
          cameraId, stream.Profile);
      }
    }
  }

  private async Task RetryConstructAsync(CameraPipeline pipeline, CancellationToken ct)
  {
    int[] delays = [1, 2, 4, 8, 15];
    for (var attempt = 0; attempt < delays.Length; attempt++)
    {
      try { await Task.Delay(TimeSpan.FromSeconds(delays[attempt]), ct); }
      catch (OperationCanceledException) { return; }

      if (_tapRegistry.GetPipeline(pipeline.CameraId, pipeline.Profile) != pipeline)
        return;

      var result = await pipeline.ConstructAsync(ct);
      if (result.IsT0)
      {
        _logger.LogInformation("Pipeline constructed for camera {CameraId} profile '{Profile}' (retry {Attempt})",
          pipeline.CameraId, pipeline.Profile, attempt + 1);
        return;
      }

      _logger.LogDebug("Retry {Attempt} failed for camera {CameraId} profile '{Profile}': {Message}",
        attempt + 1, pipeline.CameraId, pipeline.Profile, result.AsT1.Message);
    }

    _logger.LogWarning("Giving up constructing pipeline for camera {CameraId} profile '{Profile}' after retries",
      pipeline.CameraId, pipeline.Profile);
  }

  private async Task RemovePipelinesForCameraAsync(Guid cameraId)
  {
    var toRemove = _tapRegistry.Pipelines
      .Where(p => p.CameraId == cameraId)
      .ToList();

    foreach (var pipeline in toRemove)
    {
      _tapRegistry.UnregisterPipeline(pipeline.CameraId, pipeline.Profile);
      await pipeline.DisposeAsync();
      _logger.LogInformation("Removed pipeline for camera {CameraId} profile '{Profile}'",
        pipeline.CameraId, pipeline.Profile);
    }
  }

  private async Task ReconcilePipelinesForCameraAsync(Guid cameraId, CancellationToken ct)
  {
    var dataProvider = _pluginHost.DataProvider;
    var cameraResult = await dataProvider.Cameras.GetByIdAsync(cameraId, ct);
    if (cameraResult.IsT1) return;

    var camera = cameraResult.AsT0;
    var streamsResult = await dataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return;

    var desiredStreams = streamsResult.AsT0.ToDictionary(s => s.Profile);
    var existingPipelines = _tapRegistry.Pipelines
      .Where(p => p.CameraId == cameraId)
      .ToList();

    foreach (var pipeline in existingPipelines)
    {
      if (!desiredStreams.TryGetValue(pipeline.Profile, out var stream))
      {
        _tapRegistry.UnregisterPipeline(pipeline.CameraId, pipeline.Profile);
        await pipeline.DisposeAsync();
        _logger.LogInformation("Removed pipeline for camera {CameraId} profile '{Profile}' (stream removed)",
          cameraId, pipeline.Profile);
        continue;
      }

      var connectionInfo = BuildConnectionInfo(camera, stream);
      if (pipeline.ConnectionUri != connectionInfo.Uri)
      {
        _tapRegistry.UnregisterPipeline(pipeline.CameraId, pipeline.Profile);
        await pipeline.DisposeAsync();
        _logger.LogInformation("Rebuilding pipeline for camera {CameraId} profile '{Profile}' (URI changed: {Old} -> {New})",
          cameraId, pipeline.Profile, pipeline.ConnectionUri, connectionInfo.Uri);
      }
    }

    await AddPipelinesForCameraAsync(cameraId, ct);
  }

  public async Task StopAsync()
  {
    _eventCts?.Cancel();
    _eventCts?.Dispose();

    foreach (var pipeline in _tapRegistry.Pipelines)
      await pipeline.DisposeAsync();

    _logger.LogInformation("Streaming service stopped");
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync();
  }

  private async Task RebuildPipelineAsync(Camera camera, CameraStream stream)
  {
    _logger.LogInformation(
      "Rebuilding pipeline for camera {CameraId} profile '{Profile}' due to parameter mismatch",
      camera.Id, stream.Profile);

    var old = _tapRegistry.GetPipeline(camera.Id, stream.Profile);
    if (old != null)
    {
      _tapRegistry.UnregisterPipeline(camera.Id, stream.Profile);
      await old.DisposeAsync();
    }

    var captureSource = FindCaptureSource(stream.Uri);
    if (captureSource == null)
    {
      _logger.LogWarning("No capture source for stream URI '{Uri}' on camera {CameraId}",
        stream.Uri, camera.Id);
      return;
    }

    var connectionInfo = BuildConnectionInfo(camera, stream);
    var pipeline = new CameraPipeline(
      camera.Id, stream.Profile, connectionInfo,
      captureSource, _pluginHost,
      _eventBus, _logger);

    pipeline.OnParameterMismatch = () => _ = RebuildPipelineAsync(camera, stream);
    _tapRegistry.RegisterPipeline(pipeline);

    var result = await pipeline.ConstructAsync(CancellationToken.None);
    if (result.IsT1)
    {
      _logger.LogWarning(
        "Failed to construct rebuilt pipeline for camera {CameraId} profile '{Profile}': {Message}",
        camera.Id, stream.Profile, result.AsT1.Message);
    }
  }

  private ICaptureSource? FindCaptureSource(string uri)
  {
    var colonIdx = uri.IndexOf("://");
    if (colonIdx <= 0)
      return null;

    var protocol = uri[..colonIdx].ToLowerInvariant();
    return _pluginHost.CaptureSources.FirstOrDefault(cs =>
      cs.Protocol.Equals(protocol, StringComparison.OrdinalIgnoreCase));
  }

  private static CameraConnectionInfo BuildConnectionInfo(Camera camera, CameraStream stream)
  {
    Dictionary<string, string>? credentials = null;

    if (camera.Credentials is { Length: > 0 } creds)
    {
      try
      {
        credentials = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(creds);
      }
      catch (System.Text.Json.JsonException)
      {
      }
    }

    return new CameraConnectionInfo
    {
      Uri = stream.Uri,
      Credentials = credentials
    };
  }
}
