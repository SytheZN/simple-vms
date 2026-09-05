using Analyzer.MotionGridH26x;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;

namespace Tests.Unit.MotionGridH26x;

internal static class MotionGridTestHelpers
{
  internal static MotionGridH26xPlugin InitializedPlugin(Guid streamId, string codec)
  {
    var plugin = new MotionGridH26xPlugin();
    plugin.Initialize(BuildContext(streamId, codec));
    return plugin;
  }

  private static PluginContext BuildContext(Guid streamId, string codec)
  {
    return new PluginContext
    {
      Config = new InMemoryConfig(),
      Environment = new StubEnvironment(),
      LoggerFactory = new NullPluginLoggerFactory(),
      StreamTap = new StubStreamTap(),
      CameraRegistry = new SingleStreamRegistry(streamId, codec)
    };
  }

  private sealed class SingleStreamRegistry : ICameraRegistry
  {
    private readonly Guid _streamId;
    private readonly string _codec;
    public SingleStreamRegistry(Guid streamId, string codec)
    {
      _streamId = streamId;
      _codec = codec;
    }

    public Task<OneOf<IReadOnlyList<CameraInfo>, Error>> GetCamerasAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<IReadOnlyList<CameraInfo>, Error>>(new[] { BuildCamera() });

    public Task<OneOf<CameraInfo, Error>> GetCameraAsync(Guid cameraId, CancellationToken ct) =>
      Task.FromResult<OneOf<CameraInfo, Error>>(BuildCamera());

    private CameraInfo BuildCamera() => new()
    {
      Id = Guid.NewGuid(),
      Name = "test",
      Address = "test",
      ProviderId = "test",
      Capabilities = [],
      Streams =
      [
        new StreamProfile
        {
          Id = _streamId,
          Profile = "main",
          Kind = StreamKind.Quality,
          FormatId = "fmp4",
          Codec = _codec,
          Uri = "rtsp://test",
          IsRootStream = true
        }
      ]
    };
  }

  private sealed class InMemoryConfig : IConfig
  {
    private readonly Dictionary<string, string> _values = new();
    public string Get(string key, string defaultValue) =>
      _values.TryGetValue(key, out var v) ? v : defaultValue;
    public void Set(string key, string value) => _values[key] = value;
  }

  private sealed class StubEnvironment : IServerEnvironment
  {
    public string DataPath => "/tmp";
  }

  private sealed class NullPluginLoggerFactory : IPluginLoggerFactory
  {
    public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
  }

  private sealed class StubStreamTap : IStreamTap
  {
    public Task<OneOf<IDataStream, Error>> TapAsync(
      Guid cameraId, string profile, CancellationToken ct) =>
      throw new NotImplementedException();
  }
}
