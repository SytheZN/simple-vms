using Capture.Rtsp;
using Shared.Models;

namespace Tests.Unit.Streaming;

[TestFixture]
public class RtspPluginTests
{
  /// <summary>
  /// SCENARIO:
  /// RtspPlugin is created
  ///
  /// ACTION:
  /// Read metadata and protocol
  ///
  /// EXPECTED RESULT:
  /// Id is "rtsp", Protocol is "rtsp"
  /// </summary>
  [Test]
  public void Metadata_HasCorrectValues()
  {
    var plugin = new RtspPlugin();

    Assert.That(plugin.Metadata.Id, Is.EqualTo("rtsp"));
    Assert.That(plugin.Metadata.Name, Is.EqualTo("RTSP Capture"));
    Assert.That(plugin.Protocol, Is.EqualTo("rtsp"));
  }

  /// <summary>
  /// SCENARIO:
  /// RtspPlugin lifecycle methods called
  ///
  /// ACTION:
  /// Initialize, Start, Stop
  ///
  /// EXPECTED RESULT:
  /// All return success
  /// </summary>
  [Test]
  public async Task Lifecycle_AllReturnSuccess()
  {
    var plugin = new RtspPlugin();

    var initResult = plugin.Initialize(new PluginContext
    {
      Config = new FakeConfig(),
      Environment = new FakeEnvironment(),
      LoggerFactory = NullPluginLoggerFactory.Instance
    });
    Assert.That(initResult.IsT0, Is.True);

    var startResult = await plugin.StartAsync(CancellationToken.None);
    Assert.That(startResult.IsT0, Is.True);

    var stopResult = await plugin.StopAsync(CancellationToken.None);
    Assert.That(stopResult.IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// ConnectAsync called with unreachable host
  ///
  /// ACTION:
  /// Attempt connection
  ///
  /// EXPECTED RESULT:
  /// Returns error (not exception)
  /// </summary>
  [Test]
  public async Task Connect_UnreachableHost_ReturnsError()
  {
    var plugin = new RtspPlugin();

    var result = await plugin.ConnectAsync(
      new CameraConnectionInfo { Uri = "rtsp://192.0.2.1:554/unreachable" },
      new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.InternalError));
  }

  private sealed class FakeConfig : IConfig
  {
    public string Get(string key, string defaultValue) => defaultValue;
    public void Set(string key, string value) { }
  }

  private sealed class FakeEnvironment : IServerEnvironment
  {
    public string DataPath => "/tmp/test";
  }
}
