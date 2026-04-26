using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Server.Core;
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
  private readonly StreamReconciler _reconciler;
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
    _reconciler = new StreamReconciler(pluginHost, logger);
  }

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  public async Task StartAsync(CancellationToken ct)
  {
    var dataProvider = _pluginHost.DataProvider;
    await _reconciler.ReconcileAllAsync(ct);

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

      var byId = streamsResult.AsT0.ToDictionary(s => s.Id);
      foreach (var stream in streamsResult.AsT0)
      {
        var pipeline = BuildPipeline(camera, stream, byId);
        if (pipeline == null) continue;

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

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
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

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
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

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private async Task AddPipelinesForCameraAsync(Guid cameraId, CancellationToken ct)
  {
    var dataProvider = _pluginHost.DataProvider;
    var cameraResult = await dataProvider.Cameras.GetByIdAsync(cameraId, ct);
    if (cameraResult.IsT1) return;

    var camera = cameraResult.AsT0;
    var streamsResult = await dataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return;

    var byId = streamsResult.AsT0.ToDictionary(s => s.Id);
    foreach (var stream in streamsResult.AsT0)
    {
      if (_tapRegistry.GetPipeline(cameraId, stream.Profile) != null)
        continue;

      var pipeline = BuildPipeline(camera, stream, byId);
      if (pipeline == null) continue;

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

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private async Task RetryConstructAsync(IPipeline pipeline, CancellationToken ct)
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

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private async Task ReconcilePipelinesForCameraAsync(Guid cameraId, CancellationToken ct)
  {
    var dataProvider = _pluginHost.DataProvider;
    var cameraResult = await dataProvider.Cameras.GetByIdAsync(cameraId, ct);
    if (cameraResult.IsT1) return;

    await _reconciler.ReconcileCameraAsync(cameraId, ct);

    var camera = cameraResult.AsT0;
    var streamsResult = await dataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return;

    var desiredStreams = streamsResult.AsT0.ToDictionary(s => s.Profile);
    var existingPipelines = _tapRegistry.Pipelines
      .Where(p => p.CameraId == cameraId)
      .ToList();

    foreach (var pipeline in existingPipelines)
    {
      if (!desiredStreams.TryGetValue(pipeline.Profile, out var stream) || stream.DeletedAt != null)
      {
        _tapRegistry.UnregisterPipeline(pipeline.CameraId, pipeline.Profile);
        await pipeline.DisposeAsync();
        _logger.LogInformation("Removed pipeline for camera {CameraId} profile '{Profile}' (stream removed or soft-deleted)",
          cameraId, pipeline.Profile);
        continue;
      }

      if (pipeline is CameraPipeline cameraPipeline && stream.Uri != null)
      {
        var connectionInfo = BuildConnectionInfo(camera, stream);
        if (cameraPipeline.ConnectionUri != connectionInfo.Uri)
        {
          _tapRegistry.UnregisterPipeline(pipeline.CameraId, pipeline.Profile);
          await pipeline.DisposeAsync();
          _logger.LogInformation("Rebuilding pipeline for camera {CameraId} profile '{Profile}' (URI changed: {Old} -> {New})",
            cameraId, pipeline.Profile, cameraPipeline.ConnectionUri, connectionInfo.Uri);
        }
      }
      else if (pipeline is DerivedStreamPipeline derivedPipeline)
      {
        if (derivedPipeline.ProducerId != stream.ProducerId || derivedPipeline.FormatId != stream.FormatId)
        {
          _tapRegistry.UnregisterPipeline(pipeline.CameraId, pipeline.Profile);
          await pipeline.DisposeAsync();
          _logger.LogInformation("Rebuilding derived pipeline for camera {CameraId} profile '{Profile}' (producer/format changed)",
            cameraId, pipeline.Profile);
        }
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

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private async Task RebuildPipelineAsync(Camera camera, CameraStream stream)
  {
    if (stream.ProducerId != null)
    {
      _logger.LogError(
        "RebuildPipelineAsync invoked for derived stream {StreamId}; only source pipelines support parameter rebuild",
        stream.Id);
      return;
    }

    _logger.LogInformation(
      "Rebuilding pipeline for camera {CameraId} profile '{Profile}' due to parameter mismatch",
      camera.Id, stream.Profile);

    var old = _tapRegistry.GetPipeline(camera.Id, stream.Profile);
    if (old != null)
    {
      _tapRegistry.UnregisterPipeline(camera.Id, stream.Profile);
      await old.DisposeAsync();
    }

    var pipeline = BuildPipeline(camera, stream, new Dictionary<Guid, CameraStream>());
    if (pipeline == null) return;

    _tapRegistry.RegisterPipeline(pipeline);
    var result = await pipeline.ConstructAsync(CancellationToken.None);
    if (result.IsT1)
    {
      _logger.LogWarning(
        "Failed to construct rebuilt pipeline for camera {CameraId} profile '{Profile}': {Message}",
        camera.Id, stream.Profile, result.AsT1.Message);
    }
  }

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private IPipeline? BuildPipeline(Camera camera, CameraStream stream, IDictionary<Guid, CameraStream> byId)
  {
    if (stream.DeletedAt != null) return null;

    if (stream.ProducerId != null)
    {
      if (stream.ParentStreamId is not Guid parentId)
      {
        _logger.LogWarning("Derived stream {StreamId} has no ParentStreamId", stream.Id);
        return null;
      }
      if (!byId.TryGetValue(parentId, out var parent))
      {
        _logger.LogWarning("Derived stream {StreamId} parent {ParentId} not found", stream.Id, parentId);
        return null;
      }
      var analyzerIdentity = _pluginHost.Analyzers
        .FirstOrDefault(a => a.AnalyzerId == stream.ProducerId);
      if (analyzerIdentity is not IDataStreamAnalyzerStreamOutput streamOutput)
      {
        _logger.LogWarning("Analyzer '{ProducerId}' not loaded or has no stream output", stream.ProducerId);
        return null;
      }
      var format = _pluginHost.StreamFormats.FirstOrDefault(f => f.FormatId == stream.FormatId);
      if (format == null)
      {
        _logger.LogWarning("Format '{FormatId}' not loaded for derived stream {StreamId}",
          stream.FormatId, stream.Id);
        return null;
      }
      return new DerivedStreamPipeline(camera.Id, stream.Profile, parent.Profile,
        analyzerIdentity, streamOutput, format, _logger);
    }

    if (stream.Uri == null) return null;

    var captureSource = FindCaptureSource(stream.Uri);
    if (captureSource == null)
    {
      _logger.LogWarning("No capture source for stream URI '{Uri}' on camera {CameraId}",
        stream.Uri, camera.Id);
      return null;
    }

    var connectionInfo = BuildConnectionInfo(camera, stream);
    var sourcePipeline = new CameraPipeline(
      camera.Id, stream.Profile, connectionInfo,
      captureSource, _pluginHost, _eventBus, _logger);
    sourcePipeline.OnParameterMismatch = () => _ = RebuildPipelineAsync(camera, stream);
    return sourcePipeline;
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
        credentials = creds.ParseCredentialsDictionary();
      }
      catch (System.Text.Json.JsonException)
      {
      }
    }

    return new CameraConnectionInfo
    {
      Uri = stream.Uri!,
      Credentials = credentials
    };
  }
}
