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

    var activationCount = 0;

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

        _tapRegistry.RegisterPipeline(pipeline);

        if (stream.RecordingEnabled)
        {
          var result = await pipeline.ActivateAsync(ct);
          if (result.IsT0)
            activationCount++;
        }
      }
    }

    _logger.LogInformation(
      "Streaming service started: {Total} pipeline(s), {Active} active",
      _tapRegistry.Pipelines.Count, activationCount);
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
    string? username = null;
    string? password = null;

    if (camera.Credentials is { Length: > 0 } creds)
    {
      var credStr = System.Text.Encoding.UTF8.GetString(creds);
      var parts = credStr.Split(':', 2);
      if (parts.Length == 2)
      {
        username = parts[0];
        password = parts[1];
      }
    }

    return new CameraConnectionInfo
    {
      Uri = stream.Uri,
      Username = username,
      Password = password
    };
  }
}
