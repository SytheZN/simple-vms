using System.Reflection;
using Server.Plugins;
using Server.Streaming;
using Shared.Models;

namespace Tests.Unit.Streaming;

[TestFixture]
public class ReconnectionTests
{
  /// <summary>
  /// SCENARIO:
  /// CameraPipeline has a static backoff schedule
  ///
  /// ACTION:
  /// Read the backoff delays via reflection
  ///
  /// EXPECTED RESULT:
  /// Delays follow exponential pattern 1s, 2s, 4s, 8s, 16s, 30s cap
  /// </summary>
  [Test]
  public void BackoffDelays_FollowExponentialPattern()
  {
    var delays = CameraPipeline.BackoffDelays;

    Assert.That(delays.Length, Is.GreaterThanOrEqualTo(5));
    Assert.That(delays[0], Is.EqualTo(TimeSpan.FromSeconds(1)));
    Assert.That(delays[1], Is.EqualTo(TimeSpan.FromSeconds(2)));
    Assert.That(delays[2], Is.EqualTo(TimeSpan.FromSeconds(4)));
    Assert.That(delays[3], Is.EqualTo(TimeSpan.FromSeconds(8)));
    Assert.That(delays[4], Is.EqualTo(TimeSpan.FromSeconds(16)));
    Assert.That(delays[^1], Is.EqualTo(TimeSpan.FromSeconds(30)));
  }

  /// <summary>
  /// SCENARIO:
  /// CameraPipeline is constructed but never activated
  ///
  /// ACTION:
  /// Dispose the pipeline
  ///
  /// EXPECTED RESULT:
  /// Disposes cleanly without errors
  /// </summary>
  [Test]
  public async Task Dispose_BeforeActivation_CompletesCleanly()
  {
    var pipeline = CreatePipeline();
    Assert.That(pipeline.IsActive, Is.False);
    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// CameraPipeline is constructed
  ///
  /// ACTION:
  /// Check IsActive
  ///
  /// EXPECTED RESULT:
  /// IsActive is false (pipeline is a static structure, not connected)
  /// </summary>
  [Test]
  public void NewPipeline_IsNotActive()
  {
    var pipeline = CreatePipeline();
    Assert.That(pipeline.IsActive, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// CameraPipeline is constructed with camera ID and profile
  ///
  /// ACTION:
  /// Read CameraId and Profile properties
  ///
  /// EXPECTED RESULT:
  /// Returns the values passed to constructor
  /// </summary>
  [Test]
  public void Pipeline_ExposesIdentity()
  {
    var cameraId = Guid.NewGuid();
    var pipeline = new CameraPipeline(
      cameraId, "sub",
      new Shared.Models.CameraConnectionInfo { Uri = "rtsp://10.0.0.1/stream" },
      null!, new FakePluginHost(),
      new FakeEventBus(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

    Assert.That(pipeline.CameraId, Is.EqualTo(cameraId));
    Assert.That(pipeline.Profile, Is.EqualTo("sub"));
  }

  private static CameraPipeline CreatePipeline()
  {
    return new CameraPipeline(
      Guid.NewGuid(), "main",
      new Shared.Models.CameraConnectionInfo { Uri = "rtsp://192.168.1.100/stream" },
      null!, new FakePluginHost(),
      new FakeEventBus(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
  }

  private sealed class FakePluginHost : IPluginHost
  {
    public IReadOnlyList<PluginEntry> Plugins => [];
    public IDataProvider DataProvider => throw new NotImplementedException();
    public IReadOnlyList<ICaptureSource> CaptureSources => [];
    public IReadOnlyList<IStreamFormat> StreamFormats => [];
    public IReadOnlyList<ICameraProvider> CameraProviders => [];
    public IReadOnlyList<IEventFilter> EventFilters => [];
    public IReadOnlyList<INotificationSink> NotificationSinks => [];
    public IReadOnlyList<IVideoAnalyzer> VideoAnalyzers => [];
    public IReadOnlyList<IStorageProvider> StorageProviders => [];
    public IReadOnlyList<IAuthProvider> AuthProviders => [];
    public IReadOnlyList<IAuthzProvider> AuthzProviders => [];
    public IStreamFormat? FindFormat(Type inputType) => null;
    public void SetStreamTap(IStreamTap streamTap) { }
    public void SetCameraRegistry(ICameraRegistry cameraRegistry) { }
    public void SetRecordingAccess(IRecordingAccess recordingAccess) { }
    public void Discover(string pluginsPath) { }
    public void Initialize(bool dataOnly = false) { }
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
  }

  private sealed class FakeEventBus : Shared.Models.IEventBus
  {
    public Task PublishAsync<T>(T evt, CancellationToken ct)
      where T : Shared.Models.Events.ISystemEvent => Task.CompletedTask;

    public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct)
      where T : Shared.Models.Events.ISystemEvent => Empty<T>();

    private static async IAsyncEnumerable<T> Empty<T>()
    {
      await Task.CompletedTask;
      yield break;
    }
  }
}
