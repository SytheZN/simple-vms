using Server.Plugins;
using Server.Streaming;
using Shared.Models;

namespace Tests.Unit.Streaming;

[TestFixture]
public class StreamTapRegistryTests
{
  private StreamTapRegistry _registry = null!;

  [SetUp]
  public void SetUp() => _registry = new StreamTapRegistry();

  /// <summary>
  /// SCENARIO:
  /// A pipeline is registered for a camera/profile pair
  ///
  /// ACTION:
  /// Get the pipeline by camera/profile
  ///
  /// EXPECTED RESULT:
  /// Returns the registered pipeline
  /// </summary>
  [Test]
  public void RegisterPipeline_GetPipeline_ReturnsSameInstance()
  {
    var pipeline = CreatePipeline(Guid.NewGuid(), "main");
    _registry.RegisterPipeline(pipeline);

    var result = _registry.GetPipeline(pipeline.CameraId, "main");

    Assert.That(result, Is.SameAs(pipeline));
  }

  /// <summary>
  /// SCENARIO:
  /// No pipeline registered for a camera/profile pair
  ///
  /// ACTION:
  /// Tap the camera/profile
  ///
  /// EXPECTED RESULT:
  /// Returns NotFound error
  /// </summary>
  [Test]
  public async Task Tap_NoPipeline_ReturnsError()
  {
    var result = await _registry.TapAsync(Guid.NewGuid(), "main", CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// A pipeline is registered then unregistered
  ///
  /// ACTION:
  /// Get the pipeline after unregistration
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void UnregisterPipeline_GetPipeline_ReturnsNull()
  {
    var cameraId = Guid.NewGuid();
    _registry.RegisterPipeline(CreatePipeline(cameraId, "main"));
    _registry.UnregisterPipeline(cameraId, "main");

    Assert.That(_registry.GetPipeline(cameraId, "main"), Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Two pipelines registered for different profiles of the same camera
  ///
  /// ACTION:
  /// Get each pipeline by profile
  ///
  /// EXPECTED RESULT:
  /// Each returns the correct pipeline
  /// </summary>
  [Test]
  public void MultipleProfiles_ReturnsCorrectPipeline()
  {
    var cameraId = Guid.NewGuid();
    var main = CreatePipeline(cameraId, "main");
    var sub = CreatePipeline(cameraId, "sub");

    _registry.RegisterPipeline(main);
    _registry.RegisterPipeline(sub);

    Assert.That(_registry.GetPipeline(cameraId, "main"), Is.SameAs(main));
    Assert.That(_registry.GetPipeline(cameraId, "sub"), Is.SameAs(sub));
  }

  /// <summary>
  /// SCENARIO:
  /// Pipelines collection is queried
  ///
  /// ACTION:
  /// Register two pipelines and read Pipelines property
  ///
  /// EXPECTED RESULT:
  /// Contains both pipelines
  /// </summary>
  [Test]
  public void Pipelines_ReturnsAllRegistered()
  {
    _registry.RegisterPipeline(CreatePipeline(Guid.NewGuid(), "main"));
    _registry.RegisterPipeline(CreatePipeline(Guid.NewGuid(), "main"));

    Assert.That(_registry.Pipelines, Has.Count.EqualTo(2));
  }

  private static CameraPipeline CreatePipeline(Guid cameraId, string profile)
  {
    return new CameraPipeline(
      cameraId, profile,
      new CameraConnectionInfo { Uri = "rtsp://192.168.1.100/stream" },
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
    public IReadOnlyList<IDataStreamAnalyzer> Analyzers => [];
    public IReadOnlyList<IStorageProvider> StorageProviders => [];
    public IReadOnlyList<IAuthProvider> AuthProviders => [];
    public IReadOnlyList<IAuthzProvider> AuthzProviders => [];
    public IStreamFormat? FindFormat(Type inputType) => null;
    public void SetStreamTap(IStreamTap streamTap) { }
    public void SetCameraRegistry(ICameraRegistry cameraRegistry) { }
    public void SetRecordingAccess(IRecordingAccess recordingAccess) { }
    public void Discover(string pluginsPath) { }
    public void Initialize(bool dataOnly = false) { }
    public void ResetErrored() { }
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
  }

  private sealed class FakeEventBus : IEventBus
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
