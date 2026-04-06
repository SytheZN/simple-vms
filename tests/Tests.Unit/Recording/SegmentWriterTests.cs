using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Recording;
using Shared.Models;
using Shared.Models.Events;
using Shared.Models.Formats;

namespace Tests.Unit.Recording;

[TestFixture]
public class SegmentWriterTests
{
  private static readonly Guid CameraId = Guid.NewGuid();
  private static readonly Guid StreamId = Guid.NewGuid();
  private const string Profile = "main";
  private const string Codec = "h264";
  private static readonly byte[] Header = [0x00, 0x00, 0x00, 0x08, 0x66, 0x74, 0x79, 0x70];

  /// <summary>
  /// SCENARIO:
  /// Fragments arrive: non-sync, non-sync, sync, non-sync
  ///
  /// ACTION:
  /// Run SegmentWriter then cancel
  ///
  /// EXPECTED RESULT:
  /// No segment is started until the first sync point; the two leading non-sync fragments are skipped
  /// </summary>
  [Test]
  public async Task StartsSegmentOnFirstSyncPoint()
  {
    var storage = new FakeStorage();
    var data = new FakeDataProvider();
    var bus = new FakeEventBus();
    var source = new TestVideoStream(Header);

    var writer = CreateWriter(storage, data, bus, segmentDuration: 300);

    var task = writer.RunAsync(source, Header, CancellationToken.None);

    source.Emit(MakeFragment(1_000_000, sync: false));
    source.Emit(MakeFragment(2_000_000, sync: false));
    source.Emit(MakeFragment(3_000_000, sync: true));
    source.Emit(MakeFragment(4_000_000, sync: false));
    source.Complete();
    await task;

    Assert.That(storage.CreatedSegments, Has.Count.EqualTo(1));
    Assert.That(data.CreatedSegments, Has.Count.EqualTo(1));
    Assert.That(data.CreatedSegments[0].StartTime, Is.EqualTo(3_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Segment duration is 10 seconds; sync points arrive at 0s, 5s, 10s, 15s
  ///
  /// ACTION:
  /// Run SegmentWriter with fragments spanning past the duration target
  ///
  /// EXPECTED RESULT:
  /// First segment covers 0s-5s (finalized when 10s sync arrives);
  /// second segment starts at 10s; the cutover sync point is the first keyframe of the new segment
  /// </summary>
  [Test]
  public async Task FinalizesAtDurationTargetOnSyncPoint()
  {
    var storage = new FakeStorage();
    var data = new FakeDataProvider();
    var bus = new FakeEventBus();
    var source = new TestVideoStream(Header);

    var writer = CreateWriter(storage, data, bus, segmentDuration: 10);

    var task = writer.RunAsync(source, Header, CancellationToken.None);

    source.Emit(MakeFragment(0, sync: true));
    source.Emit(MakeFragment(5_000_000, sync: true));
    source.Emit(MakeFragment(10_000_000, sync: true));
    source.Emit(MakeFragment(15_000_000, sync: true));
    source.Complete();
    await task;

    Assert.That(storage.CreatedSegments, Has.Count.EqualTo(2));
    Assert.That(storage.FinalizedRefs, Has.Count.EqualTo(2));

    Assert.That(data.CreatedSegments[0].StartTime, Is.EqualTo(0UL));
    Assert.That(data.CreatedSegments[1].StartTime, Is.EqualTo(10_000_000UL));

    var firstUpdates = data.UpdatedSegments.Where(s => s.Id == data.CreatedSegments[0].Id).ToList();
    Assert.That(firstUpdates, Has.Count.GreaterThanOrEqualTo(1));
    Assert.That(firstUpdates.Last().EndTime, Is.EqualTo(10_000_000UL - 1));
  }

  /// <summary>
  /// SCENARIO:
  /// Segment duration is 10s; fragments at 0s, 3s, 6s, 9s (all under duration target)
  ///
  /// ACTION:
  /// Run SegmentWriter then complete the source
  ///
  /// EXPECTED RESULT:
  /// Only one segment is created (no cutover mid-stream); finalized once on exit
  /// </summary>
  [Test]
  public async Task DoesNotFinalizeBeforeDurationTarget()
  {
    var storage = new FakeStorage();
    var data = new FakeDataProvider();
    var bus = new FakeEventBus();
    var source = new TestVideoStream(Header);

    var writer = CreateWriter(storage, data, bus, segmentDuration: 10);

    var task = writer.RunAsync(source, Header, CancellationToken.None);

    source.Emit(MakeFragment(0, sync: true));
    source.Emit(MakeFragment(3_000_000, sync: true));
    source.Emit(MakeFragment(6_000_000, sync: true));
    source.Emit(MakeFragment(9_000_000, sync: true));
    source.Complete();
    await task;

    Assert.That(storage.CreatedSegments, Has.Count.EqualTo(1));
    Assert.That(storage.FinalizedRefs, Has.Count.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// Sync points arrive at 0s, 5s, 10s; non-sync at 7s
  ///
  /// ACTION:
  /// Run SegmentWriter with mixed sync/non-sync fragments
  ///
  /// EXPECTED RESULT:
  /// Keyframes are recorded only for sync points; non-sync fragments are written but not indexed
  /// </summary>
  [Test]
  public async Task TracksKeyframesOnlySyncPoints()
  {
    var storage = new FakeStorage();
    var data = new FakeDataProvider();
    var bus = new FakeEventBus();
    var source = new TestVideoStream(Header);

    var writer = CreateWriter(storage, data, bus, segmentDuration: 300);

    var task = writer.RunAsync(source, Header, CancellationToken.None);

    source.Emit(MakeFragment(0, sync: true));
    source.Emit(MakeFragment(5_000_000, sync: true));
    source.Emit(MakeFragment(7_000_000, sync: false));
    source.Emit(MakeFragment(10_000_000, sync: true));
    source.Complete();
    await task;

    Assert.That(data.CreatedKeyframes, Has.Count.EqualTo(3));
    Assert.That(data.CreatedKeyframes.All(k => k.SegmentId != Guid.Empty), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Init header is 8 bytes; first sync fragment is 8 bytes
  ///
  /// ACTION:
  /// Start segment and write one sync fragment
  ///
  /// EXPECTED RESULT:
  /// First keyframe byte offset equals header length (8), not 0
  /// </summary>
  [Test]
  public async Task KeyframeOffsetAccountsForHeader()
  {
    var storage = new FakeStorage();
    var data = new FakeDataProvider();
    var bus = new FakeEventBus();
    var source = new TestVideoStream(Header);

    var writer = CreateWriter(storage, data, bus, segmentDuration: 300);

    var task = writer.RunAsync(source, Header, CancellationToken.None);

    source.Emit(MakeFragment(0, sync: true));
    source.Complete();
    await task;

    Assert.That(data.CreatedKeyframes, Has.Count.EqualTo(1));
    Assert.That(data.CreatedKeyframes[0].ByteOffset, Is.EqualTo(Header.Length));
  }

  /// <summary>
  /// SCENARIO:
  /// SegmentWriter is running with an open segment
  ///
  /// ACTION:
  /// Call SealAsync
  ///
  /// EXPECTED RESULT:
  /// Current segment is finalized immediately; next sync point starts a new segment
  /// </summary>
  [Test]
  public async Task SealFinalizesCurrentSegment()
  {
    var storage = new FakeStorage();
    var data = new FakeDataProvider();
    var bus = new FakeEventBus();
    var source = new TestVideoStream(Header);

    var writer = CreateWriter(storage, data, bus, segmentDuration: 300);

    var task = writer.RunAsync(source, Header, CancellationToken.None);

    source.Emit(MakeFragment(0, sync: true));
    source.Emit(MakeFragment(1_000_000, sync: false));
    await Task.Delay(50);

    Assert.That(storage.FinalizedRefs, Has.Count.EqualTo(0));

    writer.Seal();
    await Task.Delay(50);

    Assert.That(storage.FinalizedRefs, Has.Count.EqualTo(1));
    Assert.That(bus.Published.OfType<RecordingSegmentCompleted>().Count(), Is.EqualTo(1));

    source.Emit(MakeFragment(5_000_000, sync: true));
    await Task.Delay(50);

    Assert.That(storage.CreatedSegments, Has.Count.EqualTo(2));

    source.Complete();
    await task;
  }

  /// <summary>
  /// SCENARIO:
  /// Segment finalized after cutover
  ///
  /// ACTION:
  /// Emit fragments past duration target, check published events
  ///
  /// EXPECTED RESULT:
  /// RecordingSegmentCompleted event is published with correct camera, profile, and time range
  /// </summary>
  [Test]
  public async Task PublishesEventOnFinalize()
  {
    var storage = new FakeStorage();
    var data = new FakeDataProvider();
    var bus = new FakeEventBus();
    var source = new TestVideoStream(Header);

    var writer = CreateWriter(storage, data, bus, segmentDuration: 5);

    var task = writer.RunAsync(source, Header, CancellationToken.None);

    source.Emit(MakeFragment(0, sync: true));
    source.Emit(MakeFragment(3_000_000, sync: true));
    source.Emit(MakeFragment(5_000_000, sync: true));
    source.Complete();
    await task;

    var evt = bus.Published.OfType<RecordingSegmentCompleted>().First();
    Assert.That(evt.CameraId, Is.EqualTo(CameraId));
    Assert.That(evt.Profile, Is.EqualTo(Profile));
    Assert.That(evt.StartTime, Is.EqualTo(0UL));
    Assert.That(evt.EndTime, Is.EqualTo(5_000_000UL - 1));
  }

  private static SegmentWriter CreateWriter(
    FakeStorage storage, FakeDataProvider data, FakeEventBus bus, int segmentDuration) =>
    new(CameraId, Profile, Codec, StreamId, segmentDuration,
      storage, data, bus, NullLogger.Instance);

  private static Fmp4Fragment MakeFragment(ulong ts, bool sync) => new()
  {
    Data = new byte[] { 0x00, 0x00, 0x00, 0x08, 0x6d, 0x6f, 0x6f, 0x66 },
    Timestamp = ts,
    MediaTimestamp = 0,
    IsSyncPoint = sync,
    IsHeader = false
  };

  private sealed class TestVideoStream : IVideoStream<Fmp4Fragment>
  {
    private readonly Channel<Fmp4Fragment> _channel = Channel.CreateUnbounded<Fmp4Fragment>();

    public VideoStreamInfo Info { get; } = new()
    {
      DataFormat = "fmp4",
      MimeType = "video/mp4; codecs=\"avc1.640029\"",
      Resolution = "1920x1080",
      Fps = 30
    };

    public ReadOnlyMemory<byte> Header { get; }
    public Type FrameType => typeof(Fmp4Fragment);

    public TestVideoStream(byte[] header) => Header = header;

    public void Emit(Fmp4Fragment fragment) => _channel.Writer.TryWrite(fragment);
    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<Fmp4Fragment> ReadAsync(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
      await foreach (var item in _channel.Reader.ReadAllAsync(ct))
        yield return item;
    }
  }

  private sealed class FakeStorage : IStorageProvider
  {
    public string ProviderId => "fake";
    public List<FakeSegmentHandle> CreatedSegments { get; } = [];
    public List<string> FinalizedRefs { get; } = [];

    public Task<ISegmentHandle> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct)
    {
      var handle = new FakeSegmentHandle(metadata, FinalizedRefs);
      CreatedSegments.Add(handle);
      return Task.FromResult<ISegmentHandle>(handle);
    }

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
    private readonly List<string> _finalizedRefs;

    public string SegmentRef { get; }
    public Stream Stream { get; } = new MemoryStream();

    public FakeSegmentHandle(SegmentMetadata metadata, List<string> finalizedRefs)
    {
      SegmentRef = $"{metadata.CameraId}/{metadata.Profile}/{metadata.StartTime}";
      _finalizedRefs = finalizedRefs;
    }

    public Task FinalizeAsync(CancellationToken ct)
    {
      _finalizedRefs.Add(SegmentRef);
      return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
      Stream.Dispose();
      return ValueTask.CompletedTask;
    }
  }

  private sealed class FakeDataProvider : IDataProvider
  {
    public string ProviderId => "fake";
    public ICameraRepository Cameras => throw new NotImplementedException();
    public IStreamRepository Streams => throw new NotImplementedException();
    public ISegmentRepository Segments { get; } = new FakeSegmentRepository();
    public IKeyframeRepository Keyframes { get; } = new FakeKeyframeRepository();
    public IEventRepository Events => throw new NotImplementedException();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config => throw new NotImplementedException();
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();

    public List<Segment> CreatedSegments => ((FakeSegmentRepository)Segments).Created;
    public List<Segment> UpdatedSegments => ((FakeSegmentRepository)Segments).Updated;
    public List<Keyframe> CreatedKeyframes => ((FakeKeyframeRepository)Keyframes).Created;
  }

  private sealed class FakeSegmentRepository : ISegmentRepository
  {
    public List<Segment> Created { get; } = [];
    public List<Segment> Updated { get; } = [];

    public Task<OneOf<Success, Error>> CreateAsync(Segment segment, CancellationToken ct)
    {
      Created.Add(segment);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public Task<OneOf<Success, Error>> UpdateAsync(Segment segment, CancellationToken ct)
    {
      Updated.Add(segment);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

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
    public Task<OneOf<IReadOnlyList<StreamStorageUsage>, Error>> GetSizeBreakdownAsync(CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeKeyframeRepository : IKeyframeRepository
  {
    public List<Keyframe> Created { get; } = [];

    public Task<OneOf<Success, Error>> CreateBatchAsync(IReadOnlyList<Keyframe> keyframes, CancellationToken ct)
    {
      Created.AddRange(keyframes);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

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

  private sealed class FakeEventBus : IEventBus
  {
    public List<ISystemEvent> Published { get; } = [];

    public Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent
    {
      Published.Add(evt);
      return Task.CompletedTask;
    }

    public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct) where T : ISystemEvent =>
      throw new NotImplementedException();
  }
}
