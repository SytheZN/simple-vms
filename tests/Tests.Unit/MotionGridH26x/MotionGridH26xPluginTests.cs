using Analyzer.MotionGridH26x;
using Server.Streaming;
using Shared.Models.Formats;

namespace Tests.Unit.MotionGridH26x;

[TestFixture]
public class MotionGridH26xPluginTests
{
  private static readonly Guid CameraId = Guid.Parse("11111111-2222-3333-4444-555555555555");

  /// <summary>
  /// SCENARIO:
  /// A motion grid stream is configured on a parent that carries neither H.264 nor H.265, and
  /// the host keeps retrying the start
  ///
  /// ACTION:
  /// Start the stream several times against a JPEG parent
  ///
  /// EXPECTED RESULT:
  /// Every attempt fails as unavailable and releases its tap, so the retries leave no
  /// subscribers attached to the parent
  /// </summary>
  [Test]
  public async Task StartStreamAsync_UnsupportedParent_ReleasesTapOnEveryAttempt()
  {
    await using var parent = new DataStreamFanOut<JpegUnit>(new StreamInfo { DataFormat = "mjpeg" });
    var plugin = NewPlugin(new FanOutTap(parent));

    for (var attempt = 0; attempt < 5; attempt++)
    {
      var started = await plugin.StartStreamAsync(CameraId, "main", CancellationToken.None);

      Assert.That(started.IsT1, Is.True);
      Assert.That(started.AsT1.Result, Is.EqualTo(Result.Unavailable));
    }

    Assert.That(parent.SubscriberCount, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// A motion grid stream is configured on an H.264 parent
  ///
  /// ACTION:
  /// Start the stream, then stop the plugin
  ///
  /// EXPECTED RESULT:
  /// The worker holds the tap while running and releases it when the plugin stops
  /// </summary>
  [Test]
  public async Task StartStreamAsync_H264Parent_HoldsTapUntilStopped()
  {
    await using var parent = new DataStreamFanOut<H264NalUnit>(new StreamInfo { DataFormat = "h264" });
    var plugin = NewPlugin(new FanOutTap(parent));

    var started = await plugin.StartStreamAsync(CameraId, "main", CancellationToken.None);

    Assert.That(started.IsT0, Is.True);
    Assert.That(parent.SubscriberCount, Is.EqualTo(1));

    await plugin.StopAsync(CancellationToken.None);

    Assert.That(parent.SubscriberCount, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera whose motion grid stream has run, so its filter settings are cached, is removed
  ///
  /// ACTION:
  /// Notify the plugin that the camera was removed
  ///
  /// EXPECTED RESULT:
  /// The plugin drops the cached settings for that camera
  /// </summary>
  [Test]
  public async Task OnRemovedAsync_DropsCachedCameraSettings()
  {
    await using var parent = new DataStreamFanOut<H264NalUnit>(new StreamInfo { DataFormat = "h264" });
    var plugin = NewPlugin(new FanOutTap(parent));
    await plugin.StartStreamAsync(CameraId, "main", CancellationToken.None);
    Assert.That(plugin.CachedCameraSettingsCount, Is.EqualTo(1));

    await ((IPluginCameraSettings)plugin).OnRemovedAsync(CameraId, CancellationToken.None);

    Assert.That(plugin.CachedCameraSettingsCount, Is.EqualTo(0));
    await plugin.StopAsync(CancellationToken.None);
  }

  private static MotionGridH26xPlugin NewPlugin(IStreamTap tap)
  {
    var plugin = new MotionGridH26xPlugin();
    plugin.Initialize(new PluginContext
    {
      Config = new InMemoryConfig(),
      Environment = new FakeEnvironment(),
      LoggerFactory = NullPluginLoggerFactory.Instance,
      CameraRegistry = new UnusedCameraRegistry(),
      StreamTap = tap
    });
    return plugin;
  }

  private sealed class FanOutTap(IDataStreamFanOut parent) : IStreamTap
  {
    public Task<OneOf<IDataStream, Error>> TapAsync(Guid cameraId, string profile, CancellationToken ct) =>
      Task.FromResult(OneOf<IDataStream, Error>.FromT0(parent.Subscribe(256)));
  }

  private sealed class UnusedCameraRegistry : ICameraRegistry
  {
    public Task<OneOf<IReadOnlyList<CameraInfo>, Error>> GetCamerasAsync(CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<CameraInfo, Error>> GetCameraAsync(Guid cameraId, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class InMemoryConfig : IConfig
  {
    private readonly Dictionary<string, string> _store = [];

    public string Get(string key, string defaultValue) =>
      _store.TryGetValue(key, out var val) ? val : defaultValue;

    public void Set(string key, string value) => _store[key] = value;
  }

  private sealed class FakeEnvironment : IServerEnvironment
  {
    public string DataPath => "/tmp/test";
  }
}
