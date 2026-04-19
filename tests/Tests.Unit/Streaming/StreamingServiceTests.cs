using Microsoft.Extensions.Logging;
using Server.Plugins;
using Server.Streaming;
using Shared.Models;
using Shared.Models.Events;

namespace Tests.Unit.Streaming;

[TestFixture]
public class StreamingServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// Data provider has one camera with one stream, capture source matches
  ///
  /// ACTION:
  /// Start the streaming service
  ///
  /// EXPECTED RESULT:
  /// Pipeline registered in tap registry
  /// </summary>
  [Test]
  public async Task Start_RegistersPipelines()
  {
    var cameraId = Guid.NewGuid();
    var camera = new Camera
    {
      Id = cameraId, Name = "Cam1", Address = "192.168.1.10",
      ProviderId = "onvif", Capabilities = []
    };
    var stream = new CameraStream
    {
      Id = Guid.NewGuid(), CameraId = cameraId, Profile = "main",
      FormatId = "fmp4", Uri = "rtsp://192.168.1.10/main"
    };

    var pluginHost = new TestPluginHost(
      cameras: [camera], streams: [stream],
      captureSources: [new FakeCaptureSource()]);

    var tapRegistry = new StreamTapRegistry();
    var service = new StreamingService(
      pluginHost, tapRegistry, new FakeEventBus(),
      Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
        .CreateLogger<StreamingService>());

    await service.StartAsync(CancellationToken.None);

    Assert.That(tapRegistry.GetPipeline(cameraId, "main"), Is.Not.Null);

    await service.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// No capture source matches the stream URI protocol
  ///
  /// ACTION:
  /// Start the streaming service
  ///
  /// EXPECTED RESULT:
  /// No pipeline registered (skipped)
  /// </summary>
  [Test]
  public async Task Start_NoCaptureSource_SkipsStream()
  {
    var camera = new Camera
    {
      Id = Guid.NewGuid(), Name = "Cam1", Address = "192.168.1.10",
      ProviderId = "onvif", Capabilities = []
    };
    var stream = new CameraStream
    {
      Id = Guid.NewGuid(), CameraId = camera.Id, Profile = "main",
      FormatId = "fmp4", Uri = "rtmp://192.168.1.10/main"
    };

    var pluginHost = new TestPluginHost(
      cameras: [camera], streams: [stream],
      captureSources: [new FakeCaptureSource()]);

    var tapRegistry = new StreamTapRegistry();
    var service = new StreamingService(
      pluginHost, tapRegistry, new FakeEventBus(),
      Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
        .CreateLogger<StreamingService>());

    await service.StartAsync(CancellationToken.None);

    Assert.That(tapRegistry.GetPipeline(camera.Id, "main"), Is.Null);

    await service.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Data provider returns error for cameras
  ///
  /// ACTION:
  /// Start the streaming service
  ///
  /// EXPECTED RESULT:
  /// No pipelines registered, no exception
  /// </summary>
  [Test]
  public async Task Start_CameraLoadFails_NoPipelines()
  {
    var pluginHost = new TestPluginHost(error: true);

    var tapRegistry = new StreamTapRegistry();
    var service = new StreamingService(
      pluginHost, tapRegistry, new FakeEventBus(),
      Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
        .CreateLogger<StreamingService>());

    await service.StartAsync(CancellationToken.None);

    Assert.That(tapRegistry.Pipelines, Is.Empty);

    await service.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Camera has credentials stored
  ///
  /// ACTION:
  /// Start the service
  ///
  /// EXPECTED RESULT:
  /// Pipeline is created (credentials parsed from camera)
  /// </summary>
  [Test]
  public async Task Start_WithCredentials_CreatesPipeline()
  {
    var camera = new Camera
    {
      Id = Guid.NewGuid(), Name = "Cam1", Address = "192.168.1.10",
      ProviderId = "onvif", Capabilities = [],
      Credentials = System.Text.Encoding.UTF8.GetBytes("admin:pass123")
    };
    var stream = new CameraStream
    {
      Id = Guid.NewGuid(), CameraId = camera.Id, Profile = "main",
      FormatId = "fmp4", Uri = "rtsp://192.168.1.10/main"
    };

    var pluginHost = new TestPluginHost(
      cameras: [camera], streams: [stream],
      captureSources: [new FakeCaptureSource()]);

    var tapRegistry = new StreamTapRegistry();
    var service = new StreamingService(
      pluginHost, tapRegistry, new FakeEventBus(),
      Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
        .CreateLogger<StreamingService>());

    await service.StartAsync(CancellationToken.None);

    Assert.That(tapRegistry.GetPipeline(camera.Id, "main"), Is.Not.Null);

    await service.DisposeAsync();
  }

  private sealed class TestPluginHost : IPluginHost
  {
    private readonly FakeDataProvider _dataProvider;
    private readonly IReadOnlyList<ICaptureSource> _captureSources;

    public TestPluginHost(
      IReadOnlyList<Camera>? cameras = null,
      IReadOnlyList<CameraStream>? streams = null,
      IReadOnlyList<ICaptureSource>? captureSources = null,
      bool error = false)
    {
      _dataProvider = new FakeDataProvider(cameras ?? [], streams ?? [], error);
      _captureSources = captureSources ?? [];
    }

    public IReadOnlyList<PluginEntry> Plugins => [];
    public IDataProvider DataProvider => _dataProvider;
    public IReadOnlyList<ICaptureSource> CaptureSources => _captureSources;
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
    public void ResetErrored() { }
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
  }

  private sealed class FakeDataProvider : IDataProvider
  {
    private readonly FakeCameraRepo _cameras;
    private readonly FakeStreamRepo _streams;

    public FakeDataProvider(
      IReadOnlyList<Camera> cameras,
      IReadOnlyList<CameraStream> streams,
      bool error = false)
    {
      _cameras = new FakeCameraRepo(cameras, error);
      _streams = new FakeStreamRepo(streams);
    }

    public string ProviderId => "fake";
    public ICameraRepository Cameras => _cameras;
    public IStreamRepository Streams => _streams;
    public ISegmentRepository Segments => throw new NotImplementedException();
    public IKeyframeRepository Keyframes => throw new NotImplementedException();
    public IEventRepository Events => throw new NotImplementedException();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config => throw new NotImplementedException();
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();
  }

  private sealed class FakeCameraRepo : ICameraRepository
  {
    private readonly IReadOnlyList<Camera> _cameras;
    private readonly bool _error;

    public FakeCameraRepo(IReadOnlyList<Camera> cameras, bool error) { _cameras = cameras; _error = error; }

    public Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct) =>
      _error
        ? Task.FromResult<OneOf<IReadOnlyList<Camera>, Error>>(
            Error.Create(0, 0, Result.InternalError, "fail"))
        : Task.FromResult(OneOf<IReadOnlyList<Camera>, Error>.FromT0(_cameras));

    public Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Camera, Error>> GetByAddressAsync(string address, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> CreateAsync(Camera camera, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpdateAsync(Camera camera, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeStreamRepo : IStreamRepository
  {
    private readonly IReadOnlyList<CameraStream> _streams;

    public FakeStreamRepo(IReadOnlyList<CameraStream> streams) => _streams = streams;

    public Task<OneOf<IReadOnlyList<CameraStream>, Error>> GetByCameraIdAsync(
      Guid cameraId, CancellationToken ct)
    {
      var matching = _streams.Where(s => s.CameraId == cameraId).ToList();
      return Task.FromResult(OneOf<IReadOnlyList<CameraStream>, Error>.FromT0(
        (IReadOnlyList<CameraStream>)matching));
    }

    public Task<OneOf<CameraStream, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpsertAsync(CameraStream stream, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeCaptureSource : ICaptureSource
  {
    public string Protocol => "rtsp";

    public Task<OneOf<IStreamConnection, Error>> ConnectAsync(
      CameraConnectionInfo info, CancellationToken ct) =>
      Task.FromResult<OneOf<IStreamConnection, Error>>(
        Error.Create(0, 0, Result.InternalError, "not a real source"));
  }

  private sealed class FakeEventBus : IEventBus
  {
    public Task PublishAsync<T>(T evt, CancellationToken ct)
      where T : ISystemEvent => Task.CompletedTask;

    public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct)
      where T : ISystemEvent => Empty<T>();

    private static async IAsyncEnumerable<T> Empty<T>()
    {
      await Task.CompletedTask;
      yield break;
    }
  }
}
