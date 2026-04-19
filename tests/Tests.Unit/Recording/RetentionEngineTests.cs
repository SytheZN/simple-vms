using Microsoft.Extensions.Logging.Abstractions;
using Server.Plugins;
using Server.Recording;
using Shared.Models;
using Shared.Models.Events;

namespace Tests.Unit.Recording;

[TestFixture]
public class RetentionEngineTests
{
  /// <summary>
  /// SCENARIO:
  /// Stream has RetentionMode.Days with value 7; camera has RetentionMode.Default; global is Days/30
  ///
  /// ACTION:
  /// Resolve policy
  ///
  /// EXPECTED RESULT:
  /// Stream policy wins: Days/7
  /// </summary>
  [Test]
  public void ResolvePolicy_StreamOverrideWins()
  {
    var stream = MakeStream(RetentionMode.Days, 7);
    var camera = MakeCamera(RetentionMode.Default, 0);

    var (mode, value) = RetentionEngine.ResolvePolicy(stream, camera, (RetentionMode.Days, 30));

    Assert.That(mode, Is.EqualTo(RetentionMode.Days));
    Assert.That(value, Is.EqualTo(7));
  }

  /// <summary>
  /// SCENARIO:
  /// Stream has RetentionMode.Default; camera has RetentionMode.Bytes/1000000
  ///
  /// ACTION:
  /// Resolve policy
  ///
  /// EXPECTED RESULT:
  /// Camera policy used: Bytes/1000000
  /// </summary>
  [Test]
  public void ResolvePolicy_CameraFallback()
  {
    var stream = MakeStream(RetentionMode.Default, 0);
    var camera = MakeCamera(RetentionMode.Bytes, 1_000_000);

    var (mode, value) = RetentionEngine.ResolvePolicy(stream, camera, (RetentionMode.Days, 30));

    Assert.That(mode, Is.EqualTo(RetentionMode.Bytes));
    Assert.That(value, Is.EqualTo(1_000_000));
  }

  /// <summary>
  /// SCENARIO:
  /// Stream and camera both have RetentionMode.Default
  ///
  /// ACTION:
  /// Resolve policy
  ///
  /// EXPECTED RESULT:
  /// Global default used: Percent/80
  /// </summary>
  [Test]
  public void ResolvePolicy_GlobalFallback()
  {
    var stream = MakeStream(RetentionMode.Default, 0);
    var camera = MakeCamera(RetentionMode.Default, 0);

    var (mode, value) = RetentionEngine.ResolvePolicy(stream, camera, (RetentionMode.Percent, 80));

    Assert.That(mode, Is.EqualTo(RetentionMode.Percent));
    Assert.That(value, Is.EqualTo(80));
  }

  /// <summary>
  /// SCENARIO:
  /// Retention mode is Days/7; three segments exist: 10 days old, 5 days old, 1 day old
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Only the 10-day-old segment is purged; the other two remain
  /// </summary>
  [Test]
  public async Task PurgeByDays_DeletesOldSegments()
  {
    var now = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
    var day = 86_400_000_000UL;

    var streamId = Guid.NewGuid();
    var seg10 = MakeSegment(streamId, now - 10 * day, now - 10 * day + 1000);
    var seg5 = MakeSegment(streamId, now - 5 * day, now - 5 * day + 1000);
    var seg1 = MakeSegment(streamId, now - 1 * day, now - 1 * day + 1000);

    var data = new FakeDataProvider();
    var stream = MakeStream(RetentionMode.Days, 7, streamId);
    var camera = MakeCamera(RetentionMode.Default, 0);

    data.AddCamera(camera);
    data.AddStream(stream);
    data.AddSegments(streamId, [seg10, seg5, seg1]);

    var storage = new FakeStorage();
    var engine = CreateEngine(data, storage);

    await engine.EvaluateAsync(CancellationToken.None);

    Assert.That(storage.PurgedRefs, Has.Count.EqualTo(1));
    Assert.That(storage.PurgedRefs[0], Is.EqualTo(seg10.SegmentRef));
    Assert.That(data.DeletedSegmentIds, Has.Count.EqualTo(1));
    Assert.That(data.DeletedSegmentIds[0], Is.EqualTo(seg10.Id));
  }

  /// <summary>
  /// SCENARIO:
  /// Retention mode is Bytes/100; three segments of 50 bytes each (total 150)
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Oldest segment is purged (bringing total to 100); other two remain
  /// </summary>
  [Test]
  public async Task PurgeByBytes_DeletesOldestUntilUnderLimit()
  {
    var streamId = Guid.NewGuid();
    var seg1 = MakeSegment(streamId, 1_000_000, 2_000_000, size: 50);
    var seg2 = MakeSegment(streamId, 3_000_000, 4_000_000, size: 50);
    var seg3 = MakeSegment(streamId, 5_000_000, 6_000_000, size: 50);

    var data = new FakeDataProvider();
    var stream = MakeStream(RetentionMode.Bytes, 100, streamId);
    var camera = MakeCamera(RetentionMode.Default, 0);

    data.AddCamera(camera);
    data.AddStream(stream);
    data.AddSegments(streamId, [seg1, seg2, seg3]);

    var storage = new FakeStorage();
    var engine = CreateEngine(data, storage);

    await engine.EvaluateAsync(CancellationToken.None);

    Assert.That(storage.PurgedRefs, Has.Count.EqualTo(1));
    Assert.That(storage.PurgedRefs[0], Is.EqualTo(seg1.SegmentRef));
  }

  /// <summary>
  /// SCENARIO:
  /// Retention mode is Percent/50; storage is 80% used; two segments of 100 bytes
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Oldest segment is purged to bring usage down
  /// </summary>
  [Test]
  public async Task PurgeByPercent_DeletesWhenOverThreshold()
  {
    var streamId = Guid.NewGuid();
    var seg1 = MakeSegment(streamId, 1_000_000, 2_000_000, size: 100);
    var seg2 = MakeSegment(streamId, 3_000_000, 4_000_000, size: 100);

    var data = new FakeDataProvider();
    var stream = MakeStream(RetentionMode.Percent, 50, streamId);
    var camera = MakeCamera(RetentionMode.Default, 0);

    data.AddCamera(camera);
    data.AddStream(stream);
    data.AddSegments(streamId, [seg1, seg2]);

    var storage = new FakeStorage(totalBytes: 1000, usedBytes: 800);
    var engine = CreateEngine(data, storage);

    await engine.EvaluateAsync(CancellationToken.None);

    Assert.That(storage.PurgedRefs, Has.Count.GreaterThanOrEqualTo(1));
    Assert.That(storage.PurgedRefs, Does.Contain(seg1.SegmentRef));
  }

  private static RetentionEngine CreateEngine(FakeDataProvider data, FakeStorage storage)
  {
    var host = new FakePluginHost(data, storage);
    return new RetentionEngine(host, NullLogger.Instance);
  }

  private static CameraStream MakeStream(
    RetentionMode mode, long value, Guid? streamId = null) => new()
  {
    Id = streamId ?? Guid.NewGuid(),
    CameraId = Guid.NewGuid(),
    Profile = "main",
    Kind = StreamKind.Quality,
    FormatId = "fmp4",
    Codec = "h264",
    Uri = "rtsp://test",
    RecordingEnabled = true,
    RetentionMode = mode,
    RetentionValue = value
  };

  private static Camera MakeCamera(RetentionMode mode, long value) => new()
  {
    Id = Guid.NewGuid(),
    Name = "Test",
    Address = "192.168.1.1",
    ProviderId = "test",
    RetentionMode = mode,
    RetentionValue = value
  };

  private static Segment MakeSegment(
    Guid streamId, ulong start, ulong end, long size = 1000) => new()
  {
    Id = Guid.NewGuid(),
    StreamId = streamId,
    StartTime = start,
    EndTime = end,
    SegmentRef = $"ref/{start}",
    SizeBytes = size,
    KeyframeCount = 1
  };

  private sealed class FakeStorage : IStorageProvider
  {
    private readonly long _totalBytes;
    private readonly long _usedBytes;

    public string ProviderId => "fake";
    public List<string> PurgedRefs { get; } = [];

    public FakeStorage(long totalBytes = 1_000_000_000, long usedBytes = 500_000_000)
    {
      _totalBytes = totalBytes;
      _usedBytes = usedBytes;
    }

    public Task<ISegmentHandle> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<Stream> OpenReadAsync(string segmentRef, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct)
    {
      PurgedRefs.AddRange(segmentRefs);
      return Task.CompletedTask;
    }

    public Task<StorageStats> GetStatsAsync(CancellationToken ct) =>
      Task.FromResult(new StorageStats
      {
        TotalBytes = _totalBytes,
        UsedBytes = _usedBytes,
        FreeBytes = _totalBytes - _usedBytes,
        RecordingBytes = _usedBytes
      });
  }

  private sealed class FakeDataProvider : IDataProvider
  {
    private readonly List<Camera> _cameras = [];
    private readonly Dictionary<Guid, List<CameraStream>> _streams = [];
    private readonly Dictionary<Guid, List<Segment>> _segments = [];

    public string ProviderId => "fake";
    public ICameraRepository Cameras { get; }
    public IStreamRepository Streams { get; }
    public ISegmentRepository Segments { get; }
    public IKeyframeRepository Keyframes { get; }
    public IEventRepository Events => throw new NotImplementedException();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config { get; }
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();

    public List<Guid> DeletedSegmentIds => ((FakeSegmentRepo)Segments).DeletedIds;
    public List<Guid> DeletedKeyframeSegmentIds => ((FakeKeyframeRepo)Keyframes).DeletedSegmentIds;

    public FakeDataProvider()
    {
      Cameras = new FakeCameraRepo(_cameras);
      Streams = new FakeStreamRepo(_streams);
      Segments = new FakeSegmentRepo(_segments);
      Keyframes = new FakeKeyframeRepo();
      Config = new FakeConfigRepo();
    }

    public void AddCamera(Camera camera) => _cameras.Add(camera);

    public void AddStream(CameraStream stream)
    {
      stream.CameraId = _cameras.Last().Id;
      if (!_streams.ContainsKey(stream.CameraId))
        _streams[stream.CameraId] = [];
      _streams[stream.CameraId].Add(stream);
    }

    public void AddSegments(Guid streamId, List<Segment> segments)
    {
      _segments[streamId] = segments;
    }
  }

  private sealed class FakeCameraRepo : ICameraRepository
  {
    private readonly List<Camera> _cameras;
    public FakeCameraRepo(List<Camera> cameras) => _cameras = cameras;

    public Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<IReadOnlyList<Camera>, Error>>(_cameras.ToList());

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
    private readonly Dictionary<Guid, List<CameraStream>> _streams;
    public FakeStreamRepo(Dictionary<Guid, List<CameraStream>> streams) => _streams = streams;

    public Task<OneOf<IReadOnlyList<CameraStream>, Error>> GetByCameraIdAsync(
      Guid cameraId, CancellationToken ct)
    {
      var list = _streams.GetValueOrDefault(cameraId) ?? [];
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
    private readonly Dictionary<Guid, List<Segment>> _segments;
    public List<Guid> DeletedIds { get; } = [];

    public FakeSegmentRepo(Dictionary<Guid, List<Segment>> segments) => _segments = segments;

    public Task<OneOf<Segment, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<PlaybackPoint, Error>> FindPlaybackPointAsync(
      Guid streamId, ulong timestamp, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<IReadOnlyList<Segment>, Error>> GetByTimeRangeAsync(
      Guid streamId, ulong from, ulong to, CancellationToken ct)
    {
      var list = _segments.GetValueOrDefault(streamId) ?? [];
      var filtered = list.Where(s => s.StartTime <= to && s.EndTime >= from).ToList();
      return Task.FromResult<OneOf<IReadOnlyList<Segment>, Error>>(filtered);
    }

    public Task<OneOf<IReadOnlyList<Segment>, Error>> GetOldestAsync(
      Guid streamId, int limit, CancellationToken ct)
    {
      var list = _segments.GetValueOrDefault(streamId) ?? [];
      var ordered = list.OrderBy(s => s.StartTime).Take(limit).ToList();
      return Task.FromResult<OneOf<IReadOnlyList<Segment>, Error>>(ordered);
    }

    public Task<OneOf<long, Error>> GetTotalSizeAsync(Guid streamId, CancellationToken ct)
    {
      var list = _segments.GetValueOrDefault(streamId) ?? [];
      return Task.FromResult<OneOf<long, Error>>(list.Sum(s => s.SizeBytes));
    }

    public Task<OneOf<Success, Error>> CreateAsync(Segment segment, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> UpdateAsync(Segment segment, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> DeleteBatchAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
      DeletedIds.AddRange(ids);
      foreach (var list in _segments.Values)
        list.RemoveAll(s => ids.Contains(s.Id));
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }
    public Task<OneOf<IReadOnlyList<StreamStorageUsage>, Error>> GetSizeBreakdownAsync(CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeKeyframeRepo : IKeyframeRepository
  {
    public List<Guid> DeletedSegmentIds { get; } = [];

    public Task<OneOf<Success, Error>> CreateBatchAsync(
      IReadOnlyList<Keyframe> keyframes, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> DeleteBySegmentIdsAsync(
      IReadOnlyList<Guid> segmentIds, CancellationToken ct)
    {
      DeletedSegmentIds.AddRange(segmentIds);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public Task<OneOf<IReadOnlyList<Keyframe>, Error>> GetBySegmentIdAsync(
      Guid segmentId, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<Keyframe, Error>> GetNearestAsync(
      Guid segmentId, ulong timestamp, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeConfigRepo : IConfigRepository
  {
    public Task<OneOf<string?, Error>> GetAsync(string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<string?, Error>>((string?)null);

    public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(
      string pluginId, CancellationToken ct) =>
      Task.FromResult<OneOf<IReadOnlyDictionary<string, string>, Error>>(
        new Dictionary<string, string>());

    public Task<OneOf<Success, Error>> SetAsync(
      string pluginId, string key, string value, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> DeleteAsync(
      string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
  }

  private sealed class FakePluginHost : IPluginHost
  {
    public IReadOnlyList<PluginEntry> Plugins => [];
    public IDataProvider DataProvider { get; }
    public IReadOnlyList<ICaptureSource> CaptureSources => [];
    public IReadOnlyList<IStreamFormat> StreamFormats => [];
    public IReadOnlyList<ICameraProvider> CameraProviders => [];
    public IReadOnlyList<IEventFilter> EventFilters => [];
    public IReadOnlyList<INotificationSink> NotificationSinks => [];
    public IReadOnlyList<IVideoAnalyzer> VideoAnalyzers => [];
    public IReadOnlyList<IStorageProvider> StorageProviders { get; }
    public IReadOnlyList<IAuthProvider> AuthProviders => [];
    public IReadOnlyList<IAuthzProvider> AuthzProviders => [];

    public FakePluginHost(IDataProvider data, IStorageProvider storage)
    {
      DataProvider = data;
      StorageProviders = [storage];
    }

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
}
