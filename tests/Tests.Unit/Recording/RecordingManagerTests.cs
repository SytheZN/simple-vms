using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Plugins;
using Server.Recording;
using Server.Streaming;
using Shared.Models;
using Shared.Models.Events;

namespace Tests.Unit.Recording;

[TestFixture]
public class RecordingManagerTests
{
  /// <summary>
  /// SCENARIO:
  /// A camera has a recording-enabled stream; no writer exists yet
  ///
  /// ACTION:
  /// Call ReconcileAsync for the camera
  ///
  /// EXPECTED RESULT:
  /// A writer is created (WriterCount increases to 1)
  /// </summary>
  [Test]
  public async Task ReconcileAsync_RecordingEnabledStream_StartsWriter()
  {
    var cameraId = Guid.NewGuid();
    var streamId = Guid.NewGuid();
    var data = new FakeDataProvider();
    var camera = MakeCamera(cameraId);
    var stream = MakeStream(streamId, cameraId, recordingEnabled: true);
    data.AddCamera(camera);
    data.AddStream(stream);

    var storage = new FakeStorage();
    var eventBus = new FakeEventBus();
    var tapRegistry = new StreamTapRegistry();
    var host = new FakePluginHost(data, storage);

    var manager = new RecordingManager(host, tapRegistry, eventBus,
      NullLogger.Instance);

    await manager.ReconcileAsync(cameraId, CancellationToken.None);

    Assert.That(manager.WriterCount, Is.EqualTo(1));

    await manager.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// A camera has a writer already running for a stream that is still recording-enabled
  ///
  /// ACTION:
  /// Call ReconcileAsync for the camera again
  ///
  /// EXPECTED RESULT:
  /// The existing writer is left untouched (count stays at 1, no restart)
  /// </summary>
  [Test]
  public async Task ReconcileAsync_ExistingWriterStillEnabled_LeavesUntouched()
  {
    var cameraId = Guid.NewGuid();
    var streamId = Guid.NewGuid();
    var data = new FakeDataProvider();
    var camera = MakeCamera(cameraId);
    var stream = MakeStream(streamId, cameraId, recordingEnabled: true);
    data.AddCamera(camera);
    data.AddStream(stream);

    var storage = new FakeStorage();
    var eventBus = new FakeEventBus();
    var tapRegistry = new StreamTapRegistry();
    var host = new FakePluginHost(data, storage);

    var manager = new RecordingManager(host, tapRegistry, eventBus,
      NullLogger.Instance);

    await manager.ReconcileAsync(cameraId, CancellationToken.None);
    Assert.That(manager.WriterCount, Is.EqualTo(1));

    await manager.ReconcileAsync(cameraId, CancellationToken.None);

    Assert.That(manager.WriterCount, Is.EqualTo(1),
      "Writer should remain, not be duplicated");

    await manager.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// A camera has a writer running, then RecordingEnabled is set to false in the database
  ///
  /// ACTION:
  /// Call ReconcileAsync for the camera
  ///
  /// EXPECTED RESULT:
  /// The writer is stopped (count drops to 0)
  /// </summary>
  [Test]
  public async Task ReconcileAsync_RecordingDisabled_StopsWriter()
  {
    var cameraId = Guid.NewGuid();
    var streamId = Guid.NewGuid();
    var data = new FakeDataProvider();
    var camera = MakeCamera(cameraId);
    var stream = MakeStream(streamId, cameraId, recordingEnabled: true);
    data.AddCamera(camera);
    data.AddStream(stream);

    var storage = new FakeStorage();
    var eventBus = new FakeEventBus();
    var tapRegistry = new StreamTapRegistry();
    var host = new FakePluginHost(data, storage);

    var manager = new RecordingManager(host, tapRegistry, eventBus,
      NullLogger.Instance);

    await manager.ReconcileAsync(cameraId, CancellationToken.None);
    Assert.That(manager.WriterCount, Is.EqualTo(1));

    stream.RecordingEnabled = false;

    await manager.ReconcileAsync(cameraId, CancellationToken.None);

    Assert.That(manager.WriterCount, Is.EqualTo(0));

    await manager.DisposeAsync();
  }

  private static Camera MakeCamera(Guid id) => new()
  {
    Id = id,
    Name = "Test",
    Address = "192.168.1.1",
    ProviderId = "test"
  };

  private static CameraStream MakeStream(Guid id, Guid cameraId, bool recordingEnabled) => new()
  {
    Id = id,
    CameraId = cameraId,
    Profile = "main",
    Kind = StreamKind.Quality,
    FormatId = "fmp4",
    Codec = "h264",
    Uri = "rtsp://test",
    RecordingEnabled = recordingEnabled
  };

  private sealed class FakeEventBus : IEventBus
  {
    public Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent =>
      Task.CompletedTask;

    public async IAsyncEnumerable<T> SubscribeAsync<T>(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
      where T : ISystemEvent
    {
      await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
      yield break;
    }
  }

  private sealed class FakeStorage : IStorageProvider
  {
    public string ProviderId => "fake";

    public Task<ISegmentHandle> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct) =>
      Task.FromResult<ISegmentHandle>(new FakeSegmentHandle(metadata));

    public Task<Stream> OpenReadAsync(string segmentRef, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct) =>
      Task.CompletedTask;

    public Task<StorageStats> GetStatsAsync(CancellationToken ct) =>
      Task.FromResult(new StorageStats
      {
        TotalBytes = 1_000_000_000,
        UsedBytes = 500_000_000,
        FreeBytes = 500_000_000,
        RecordingBytes = 400_000_000
      });
  }

  private sealed class FakeSegmentHandle : ISegmentHandle
  {
    public string SegmentRef { get; }
    public Stream Stream { get; } = new MemoryStream();

    public FakeSegmentHandle(SegmentMetadata metadata)
    {
      SegmentRef = $"fake/{metadata.StartTime}";
    }

    public Task FinalizeAsync(CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync()
    {
      Stream.Dispose();
      return ValueTask.CompletedTask;
    }
  }

  private sealed class FakeDataProvider : IDataProvider
  {
    private readonly List<Camera> _cameras = [];
    private readonly Dictionary<Guid, List<CameraStream>> _streams = [];

    public string ProviderId => "fake";
    public ICameraRepository Cameras { get; }
    public IStreamRepository Streams { get; }
    public ISegmentRepository Segments { get; } = new FakeSegmentRepo();
    public IKeyframeRepository Keyframes { get; } = new FakeKeyframeRepo();
    public IEventRepository Events => throw new NotImplementedException();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config { get; } = new FakeConfigRepo();
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();

    public FakeDataProvider()
    {
      Cameras = new FakeCameraRepo(_cameras);
      Streams = new FakeStreamRepo(_streams);
    }

    public void AddCamera(Camera camera) => _cameras.Add(camera);

    public void AddStream(CameraStream stream)
    {
      if (!_streams.ContainsKey(stream.CameraId))
        _streams[stream.CameraId] = [];
      _streams[stream.CameraId].Add(stream);
    }
  }

  private sealed class FakeCameraRepo(List<Camera> cameras) : ICameraRepository
  {
    public Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<IReadOnlyList<Camera>, Error>>(cameras.ToList());

    public Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct)
    {
      var cam = cameras.FirstOrDefault(c => c.Id == id);
      return cam != null
        ? Task.FromResult<OneOf<Camera, Error>>(cam)
        : Task.FromResult<OneOf<Camera, Error>>(
          Error.Create(0, 0, Result.NotFound, "Not found"));
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

  private sealed class FakeStreamRepo(Dictionary<Guid, List<CameraStream>> streams) : IStreamRepository
  {
    public Task<OneOf<IReadOnlyList<CameraStream>, Error>> GetByCameraIdAsync(
      Guid cameraId, CancellationToken ct)
    {
      var list = streams.GetValueOrDefault(cameraId) ?? [];
      return Task.FromResult<OneOf<IReadOnlyList<CameraStream>, Error>>(list.ToList());
    }

    public Task<OneOf<CameraStream, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpsertAsync(CameraStream stream, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeSegmentRepo : ISegmentRepository
  {
    public Task<OneOf<Success, Error>> CreateAsync(Segment segment, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
    public Task<OneOf<Success, Error>> UpdateAsync(Segment segment, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
    public Task<OneOf<Segment, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<IReadOnlyList<Segment>, Error>> GetByTimeRangeAsync(
      Guid streamId, ulong from, ulong to, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<PlaybackPoint, Error>> FindPlaybackPointAsync(
      Guid streamId, ulong timestamp, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<IReadOnlyList<Segment>, Error>> GetOldestAsync(
      Guid streamId, int limit, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<long, Error>> GetTotalSizeAsync(Guid streamId, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteBatchAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeKeyframeRepo : IKeyframeRepository
  {
    public Task<OneOf<Success, Error>> CreateBatchAsync(
      IReadOnlyList<Keyframe> keyframes, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
    public Task<OneOf<IReadOnlyList<Keyframe>, Error>> GetBySegmentIdAsync(
      Guid segmentId, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Keyframe, Error>> GetNearestAsync(
      Guid segmentId, ulong timestamp, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteBySegmentIdsAsync(
      IReadOnlyList<Guid> segmentIds, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeConfigRepo : IConfigRepository
  {
    public Task<OneOf<string?, Error>> GetAsync(string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<string?, Error>>((string?)null);
    public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(
      string pluginId, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> SetAsync(
      string pluginId, string key, string value, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteAsync(
      string pluginId, string key, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakePluginHost(FakeDataProvider data, FakeStorage storage) : IPluginHost
  {
    public IReadOnlyList<PluginEntry> Plugins => [];
    public IDataProvider DataProvider => data;
    public IReadOnlyList<ICaptureSource> CaptureSources => [];
    public IReadOnlyList<IStreamFormat> StreamFormats => [];
    public IReadOnlyList<ICameraProvider> CameraProviders => [];
    public IReadOnlyList<IEventFilter> EventFilters => [];
    public IReadOnlyList<INotificationSink> NotificationSinks => [];
    public IReadOnlyList<IVideoAnalyzer> VideoAnalyzers => [];
    public IReadOnlyList<IStorageProvider> StorageProviders => [storage];
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
}
