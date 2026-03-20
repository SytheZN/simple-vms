using Server.Core;
using Server.Streaming;
using Shared.Models;

namespace Tests.Unit.Streaming;

[TestFixture]
public class CameraRegistryImplTests
{
  /// <summary>
  /// SCENARIO:
  /// Data provider has one camera with one stream
  ///
  /// ACTION:
  /// GetCamerasAsync
  ///
  /// EXPECTED RESULT:
  /// Returns one CameraInfo with the stream profile
  /// </summary>
  [Test]
  public async Task GetCameras_ReturnsInfoWithStreams()
  {
    var cameraId = Guid.NewGuid();
    var camera = new Camera
    {
      Id = cameraId, Name = "Cam1", Address = "192.168.1.10",
      ProviderId = "onvif", Capabilities = ["ptz"]
    };
    var stream = new CameraStream
    {
      Id = Guid.NewGuid(), CameraId = cameraId, Profile = "main",
      FormatId = "fmp4", Uri = "rtsp://192.168.1.10/main",
      Codec = "h264", Resolution = "1920x1080", Fps = 30
    };

    var dp = new FakeDataProvider([camera], [stream]);
    var registry = new CameraRegistryImpl(dp, new CameraStatusTracker());

    var result = await registry.GetCamerasAsync(CancellationToken.None);

    Assert.That(result, Has.Count.EqualTo(1));
    Assert.That(result[0].Name, Is.EqualTo("Cam1"));
    Assert.That(result[0].Streams, Has.Count.EqualTo(1));
    Assert.That(result[0].Streams[0].Profile, Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// Data provider returns error for cameras
  ///
  /// ACTION:
  /// GetCamerasAsync
  ///
  /// EXPECTED RESULT:
  /// Returns empty list
  /// </summary>
  [Test]
  public async Task GetCameras_OnError_ReturnsEmpty()
  {
    var dp = new FakeDataProvider(error: true);
    var registry = new CameraRegistryImpl(dp, new CameraStatusTracker());

    var result = await registry.GetCamerasAsync(CancellationToken.None);

    Assert.That(result, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Data provider has a camera
  ///
  /// ACTION:
  /// GetCameraAsync with matching ID
  ///
  /// EXPECTED RESULT:
  /// Returns the CameraInfo
  /// </summary>
  [Test]
  public async Task GetCamera_Found_ReturnsInfo()
  {
    var cameraId = Guid.NewGuid();
    var camera = new Camera
    {
      Id = cameraId, Name = "Cam2", Address = "192.168.1.20",
      ProviderId = "onvif", Capabilities = []
    };

    var dp = new FakeDataProvider([camera], []);
    var registry = new CameraRegistryImpl(dp, new CameraStatusTracker());

    var result = await registry.GetCameraAsync(cameraId, CancellationToken.None);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.Name, Is.EqualTo("Cam2"));
  }

  /// <summary>
  /// SCENARIO:
  /// Data provider has no matching camera
  ///
  /// ACTION:
  /// GetCameraAsync with unknown ID
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public async Task GetCamera_NotFound_ReturnsNull()
  {
    var dp = new FakeDataProvider([], []);
    var registry = new CameraRegistryImpl(dp, new CameraStatusTracker());

    var result = await registry.GetCameraAsync(Guid.NewGuid(), CancellationToken.None);

    Assert.That(result, Is.Null);
  }

  private sealed class FakeDataProvider : IDataProvider
  {
    private readonly FakeCameraRepo _cameras;
    private readonly FakeStreamRepo _streams;

    public FakeDataProvider(
      IReadOnlyList<Camera>? cameras = null,
      IReadOnlyList<CameraStream>? streams = null,
      bool error = false)
    {
      _cameras = new FakeCameraRepo(cameras ?? [], error);
      _streams = new FakeStreamRepo(streams ?? []);
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

    public FakeCameraRepo(IReadOnlyList<Camera> cameras, bool error = false)
    {
      _cameras = cameras;
      _error = error;
    }

    public Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct)
    {
      if (_error)
        return Task.FromResult<OneOf<IReadOnlyList<Camera>, Error>>(
          Error.Create(0, 0, Result.InternalError, "fail"));
      return Task.FromResult(OneOf<IReadOnlyList<Camera>, Error>.FromT0(_cameras));
    }

    public Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct)
    {
      var cam = _cameras.FirstOrDefault(c => c.Id == id);
      if (cam == null)
        return Task.FromResult<OneOf<Camera, Error>>(
          Error.Create(0, 0, Result.NotFound, "not found"));
      return Task.FromResult(OneOf<Camera, Error>.FromT0(cam));
    }

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
}
