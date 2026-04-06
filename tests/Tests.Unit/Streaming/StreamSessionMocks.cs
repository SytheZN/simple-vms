using System.Threading.Channels;
using Server.Plugins;
using Server.Streaming;
using Shared.Models;
using Shared.Models.Events;
using Shared.Models.Formats;
using Shared.Protocol;

namespace Tests.Unit.Streaming;

internal sealed class TestStreamSink : IStreamSink
{
  private bool _open = true;
  private readonly TaskCompletionSource _gopReceived = new();
  private readonly TaskCompletionSource _initReceived = new();

  public bool IsOpen => _open;
  public List<StreamStatus> Statuses { get; } = [];
  public int InitCount { get; private set; }
  public int GopCount { get; private set; }
  public int GapCount { get; private set; }
  public List<(GopFlags Flags, string Profile, ulong Timestamp, int DataLength)> Gops { get; } = [];

  public void Close() => _open = false;

  public Task WaitForGopAsync(CancellationToken ct) =>
    _gopReceived.Task.WaitAsync(ct);

  public Task WaitForInitAsync(CancellationToken ct) =>
    _initReceived.Task.WaitAsync(ct);

  public Task SendInitAsync(string profile, ReadOnlyMemory<byte> data, CancellationToken ct)
  {
    InitCount++;
    _initReceived.TrySetResult();
    return Task.CompletedTask;
  }

  public Task SendGopAsync(GopFlags flags, string profile, ulong timestamp,
    ReadOnlyMemory<byte> data, CancellationToken ct)
  {
    GopCount++;
    Gops.Add((flags, profile, timestamp, data.Length));
    _gopReceived.TrySetResult();
    return Task.CompletedTask;
  }

  public Task SendStatusAsync(StreamStatus status, CancellationToken ct)
  {
    Statuses.Add(status);
    return Task.CompletedTask;
  }

  public Task SendGapAsync(ulong from, ulong to, CancellationToken ct)
  {
    GapCount++;
    return Task.CompletedTask;
  }
}

internal sealed class StubEventBus : IEventBus
{
  public Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent =>
    Task.CompletedTask;

  public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct) where T : ISystemEvent =>
    Empty<T>();

  private static async IAsyncEnumerable<T> Empty<T>()
  {
    await Task.CompletedTask;
    yield break;
  }
}

internal sealed class StubCaptureSource(Channel<Fmp4Fragment>? channel = null) : ICaptureSource
{
  public string Protocol => "rtsp";

  public Task<OneOf<IStreamConnection, Error>> ConnectAsync(
    CameraConnectionInfo info, CancellationToken ct) =>
    Task.FromResult(OneOf<IStreamConnection, Error>.FromT0(
      (IStreamConnection)new StubStreamConnection(channel)));
}

internal sealed class StubStreamConnection : IStreamConnection
{
  private readonly TaskCompletionSource _tcs = new();
  private readonly Channel<Fmp4Fragment> _ch;

  public StreamInfo Info { get; } = new() { DataFormat = "fmp4", Fps = 30 };
  public IDataStream DataStream { get; }
  public Task Completed => _tcs.Task;

  public StubStreamConnection(Channel<Fmp4Fragment>? channel = null)
  {
    _ch = channel ?? Channel.CreateUnbounded<Fmp4Fragment>();
    DataStream = new StubFmp4DataStream(_ch.Reader);
  }

  public ValueTask DisposeAsync()
  {
    _ch.Writer.TryComplete();
    _tcs.TrySetResult();
    return ValueTask.CompletedTask;
  }
}

internal sealed class StubFmp4DataStream(ChannelReader<Fmp4Fragment> reader) : IDataStream<Fmp4Fragment>
{
  public StreamInfo Info { get; } = new() { DataFormat = "fmp4", Fps = 30 };
  public Type FrameType => typeof(Fmp4Fragment);

  public async IAsyncEnumerable<Fmp4Fragment> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var item in reader.ReadAllAsync(ct))
      yield return item;
  }
}

internal sealed class SessionTestPluginHost(
  IDataProvider? dataProvider = null,
  IReadOnlyList<IStorageProvider>? storageProviders = null,
  IReadOnlyList<IStreamFormat>? streamFormats = null) : IPluginHost
{
  public IReadOnlyList<PluginEntry> Plugins => [];
  public IDataProvider DataProvider => dataProvider!;
  public IReadOnlyList<ICaptureSource> CaptureSources => [];
  public IReadOnlyList<IStreamFormat> StreamFormats => streamFormats ?? [];
  public IReadOnlyList<ICameraProvider> CameraProviders => [];
  public IReadOnlyList<IEventFilter> EventFilters => [];
  public IReadOnlyList<INotificationSink> NotificationSinks => [];
  public IReadOnlyList<IVideoAnalyzer> VideoAnalyzers => [];
  public IReadOnlyList<IStorageProvider> StorageProviders =>
    storageProviders ?? [new StubStorageProvider()];
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

internal sealed class StubDataProvider(
  IStreamRepository? streams = null,
  ISegmentRepository? segments = null) : IDataProvider
{
  public string ProviderId => "test";
  public ICameraRepository Cameras => null!;
  public IStreamRepository Streams => streams ?? new StubStreamRepository();
  public ISegmentRepository Segments => segments ?? new StubSegmentRepository();
  public IKeyframeRepository Keyframes => null!;
  public IEventRepository Events => null!;
  public IClientRepository Clients => null!;
  public IConfigRepository Config => null!;
  public IDataStore GetDataStore(string pluginId) => null!;
}

internal sealed class StubStreamRepository(
  IReadOnlyList<CameraStream>? streams = null,
  Error? error = null) : IStreamRepository
{
  public Task<OneOf<IReadOnlyList<CameraStream>, Error>> GetByCameraIdAsync(
    Guid cameraId, CancellationToken ct)
  {
    if (error.HasValue) return Task.FromResult<OneOf<IReadOnlyList<CameraStream>, Error>>(error.Value);
    IReadOnlyList<CameraStream> list = streams ?? [];
    return Task.FromResult(OneOf<IReadOnlyList<CameraStream>, Error>.FromT0(list));
  }

  public Task<OneOf<CameraStream, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
    Task.FromResult<OneOf<CameraStream, Error>>(
      Error.Create(0, 0, Result.NotFound, "not found"));

  public Task<OneOf<Success, Error>> UpsertAsync(CameraStream stream, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}

internal sealed class StubSegmentRepository(
  PlaybackPoint? playbackPoint = null,
  Segment? segment = null,
  Error? playbackError = null) : ISegmentRepository
{
  public Task<OneOf<PlaybackPoint, Error>> FindPlaybackPointAsync(
    Guid streamId, ulong timestamp, CancellationToken ct)
  {
    if (playbackError.HasValue) return Task.FromResult<OneOf<PlaybackPoint, Error>>(playbackError.Value);
    if (playbackPoint != null) return Task.FromResult<OneOf<PlaybackPoint, Error>>(playbackPoint);
    return Task.FromResult<OneOf<PlaybackPoint, Error>>(
      Error.Create(0, 0, Result.NotFound, "not found"));
  }

  public Task<OneOf<Segment, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    if (segment != null) return Task.FromResult<OneOf<Segment, Error>>(segment);
    return Task.FromResult<OneOf<Segment, Error>>(
      Error.Create(0, 0, Result.NotFound, "not found"));
  }

  public Task<OneOf<IReadOnlyList<Segment>, Error>> GetByTimeRangeAsync(
    Guid streamId, ulong from, ulong to, CancellationToken ct) =>
    Task.FromResult(
      OneOf<IReadOnlyList<Segment>, Error>.FromT0((IReadOnlyList<Segment>)Array.Empty<Segment>()));

  public Task<OneOf<IReadOnlyList<Segment>, Error>> GetOldestAsync(
    Guid streamId, int limit, CancellationToken ct) =>
    Task.FromResult(
      OneOf<IReadOnlyList<Segment>, Error>.FromT0((IReadOnlyList<Segment>)Array.Empty<Segment>()));

  public Task<OneOf<long, Error>> GetTotalSizeAsync(Guid streamId, CancellationToken ct) =>
    Task.FromResult<OneOf<long, Error>>(0L);

  public Task<OneOf<Success, Error>> CreateAsync(Segment seg, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> UpdateAsync(Segment seg, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> DeleteBatchAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<IReadOnlyList<StreamStorageUsage>, Error>> GetSizeBreakdownAsync(CancellationToken ct) =>
    Task.FromResult(
      OneOf<IReadOnlyList<StreamStorageUsage>, Error>.FromT0(
        (IReadOnlyList<StreamStorageUsage>)Array.Empty<StreamStorageUsage>()));
}

internal sealed class StubStorageProvider : IStorageProvider
{
  public string ProviderId => "test";

  public Task<ISegmentHandle> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct) =>
    throw new NotImplementedException();

  public Task<Stream> OpenReadAsync(string segmentRef, CancellationToken ct) =>
    Task.FromResult<Stream>(new MemoryStream(new byte[100]));

  public Task PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct) =>
    Task.CompletedTask;

  public Task<StorageStats> GetStatsAsync(CancellationToken ct) =>
    Task.FromResult(new StorageStats
    {
      TotalBytes = 1000, UsedBytes = 500, FreeBytes = 500, RecordingBytes = 100
    });
}
