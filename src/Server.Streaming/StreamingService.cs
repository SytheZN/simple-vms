using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Server.Core;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Entities;
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
        else
        {
          await PersistMuxInfoAsync(pipeline, ct);
        }
      }
    }

    _eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    WatchCameraAdded(_eventCts.Token);
    WatchCameraRemoved(_eventCts.Token);
    WatchCameraConfigChanged(_eventCts.Token);
    WatchPipelineConfigMismatch(_eventCts.Token);

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
        try { await ReconcilePipelinesForCameraAsync(evt.CameraId, null, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to reconcile pipelines for camera {CameraId}", evt.CameraId);
        }
      }
    }, ct);
  }

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private void WatchPipelineConfigMismatch(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<PipelineConfigMismatch>(ct))
      {
        try
        {
          var affected = _tapRegistry.Pipelines
            .OfType<CameraPipeline>()
            .Where(p => p.ConnectionUri == evt.Uri)
            .ToList();

          foreach (var cameraId in affected.Select(p => p.CameraId).Distinct())
            await ReconcilePipelinesForCameraAsync(cameraId, evt.Uri, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to rebuild pipelines for stream {Uri}", evt.Uri);
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
        await PersistMuxInfoAsync(pipeline, ct);
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
        await PersistMuxInfoAsync(pipeline, ct);
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
  private async Task ReconcilePipelinesForCameraAsync(
    Guid cameraId, string? mismatchedUri, CancellationToken ct)
  {
    var dataProvider = _pluginHost.DataProvider;
    var cameraResult = await dataProvider.Cameras.GetByIdAsync(cameraId, ct);
    if (cameraResult.IsT1) return;

    await _reconciler.ReconcileCameraAsync(cameraId, ct);

    var camera = cameraResult.AsT0;
    var streamsResult = await dataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return;

    var desiredStreams = streamsResult.AsT0.ToDictionary(s => s.Profile);
    var byId = streamsResult.AsT0.ToDictionary(s => s.Id);
    var existingPipelines = _tapRegistry.Pipelines
      .Where(p => p.CameraId == cameraId)
      .ToList();

    var dropped = new HashSet<string>();

    foreach (var pipeline in existingPipelines)
    {
      var reason = DropReason(pipeline, camera, desiredStreams, mismatchedUri);
      if (reason == null) continue;

      dropped.Add(pipeline.Profile);
      _logger.LogInformation("Dropping pipeline for camera {CameraId} profile '{Profile}': {Reason}",
        cameraId, pipeline.Profile, reason);
    }

    foreach (var pipeline in existingPipelines.OfType<DerivedStreamPipeline>())
    {
      if (dropped.Contains(pipeline.Profile)) continue;
      if (!desiredStreams.TryGetValue(pipeline.Profile, out var stream)) continue;

      var root = Server.Core.StreamHierarchy.ResolveRootStream(
        stream, id => byId.TryGetValue(id, out var v) ? v : null, _logger);
      if (!dropped.Contains(root.Profile)) continue;

      dropped.Add(pipeline.Profile);
      _logger.LogInformation(
        "Dropping derived pipeline for camera {CameraId} profile '{Profile}': parent '{Parent}' was dropped",
        cameraId, pipeline.Profile, root.Profile);
    }

    foreach (var pipeline in existingPipelines.Where(p => dropped.Contains(p.Profile)))
    {
      _tapRegistry.UnregisterPipeline(pipeline.CameraId, pipeline.Profile);
      await pipeline.DisposeAsync();
    }

    await AddPipelinesForCameraAsync(cameraId, ct);
  }

  private string? DropReason(
    IPipeline pipeline, Camera camera, Dictionary<string, CameraStream> desiredStreams,
    string? mismatchedUri)
  {
    if (!desiredStreams.TryGetValue(pipeline.Profile, out var stream) || stream.DeletedAt != null)
      return "stream removed or soft-deleted";

    if (pipeline is CameraPipeline source)
    {
      if (source.ConnectionUri == mismatchedUri)
        return "the camera is no longer sending what the pipeline was constructed for";

      if (stream.Uri == null)
        return null;

      var uri = BuildConnectionInfo(camera, stream).Uri;
      return source.ConnectionUri == uri ? null : $"URI changed: {source.ConnectionUri} -> {uri}";
    }

    if (pipeline is DerivedStreamPipeline derived
      && (derived.ProducerId != stream.ProducerId || derived.FormatId != stream.FormatId))
      return "producer or format changed";

    return null;
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

  private async Task PersistMuxInfoAsync(IPipeline pipeline, CancellationToken ct)
  {
    var info = pipeline.MuxInfo;
    if (info == null) return;

    var streams = await _pluginHost.DataProvider.Streams.GetByCameraIdAsync(pipeline.CameraId, ct);
    if (streams.IsT1) return;

    var row = streams.AsT0.FirstOrDefault(s => s.Profile == pipeline.Profile);
    if (row == null) return;

    var dirty = false;
    if (row.Resolution != info.Resolution) { row.Resolution = info.Resolution; dirty = true; }
    var fps = info.Fps == 0 ? (decimal?)null : info.Fps;
    if (row.Fps != fps) { row.Fps = fps; dirty = true; }
    if (dirty)
      await _pluginHost.DataProvider.Streams.UpsertAsync(row, ct);
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
      var recordable = streamOutput.GetDerivedStreams(camera.Id)
        .FirstOrDefault(s => s.Profile == stream.Profile)?.Recordable ?? true;

      return new DerivedStreamPipeline(camera.Id, stream.Profile, parent.Profile,
        analyzerIdentity, streamOutput, format, recordable, _logger);
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
    return new CameraPipeline(
      camera.Id, stream.Profile, connectionInfo,
      captureSource, _pluginHost, _eventBus, _logger);
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
