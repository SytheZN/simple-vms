using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;

namespace Server.Streaming;

public sealed class StreamingService : IAsyncDisposable
{
  private readonly IPluginHost _pluginHost;
  private readonly StreamTapRegistry _tapRegistry;
  private readonly IEventBus _eventBus;
  private readonly ILogger<StreamingService> _logger;

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

    _logger.LogInformation("Streaming service started: {Count} pipeline(s) registered",
      _tapRegistry.Pipelines.Count);
  }

  public async Task StopAsync()
  {
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
