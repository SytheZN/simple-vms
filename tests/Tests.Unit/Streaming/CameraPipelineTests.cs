using System.Threading.Channels;
using Server.Streaming;
using Shared.Models.Events;
using Shared.Models.Formats;
using Tests.Unit.Mocks;

namespace Tests.Unit.Streaming;

[TestFixture]
public class CameraPipelineTests
{
  /// <summary>
  /// SCENARIO:
  /// Pipeline is constructed with a mock capture source that succeeds
  ///
  /// ACTION:
  /// Construct the pipeline
  ///
  /// EXPECTED RESULT:
  /// IsConstructed becomes true
  /// </summary>
  [Test]
  public async Task Construct_SuccessfulConnect_BecomesConstructed()
  {
    var pipeline = CreatePipeline();

    var result = await pipeline.ConstructAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(pipeline.IsConstructed, Is.True);

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Capture source returns an error on connect
  ///
  /// ACTION:
  /// Construct the pipeline
  ///
  /// EXPECTED RESULT:
  /// Returns the error, IsConstructed remains false
  /// </summary>
  [Test]
  public async Task Construct_ConnectFails_ReturnsError()
  {
    var captureSource = new MockCaptureSource
    {
      ConnectError = Error.Create(ModuleIds.PluginRtspCapture, 0x01, Result.InternalError, "timeout")
    };
    var pipeline = CreatePipeline(captureSource: captureSource);

    var result = await pipeline.ConstructAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(pipeline.IsConstructed, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is constructed and a subscriber calls SubscribeDataAsync
  ///
  /// ACTION:
  /// Subscribe to the data stream
  ///
  /// EXPECTED RESULT:
  /// Returns an IDataStream subscriber from the fan-out
  /// </summary>
  [Test]
  public async Task SubscribeData_WhenConstructed_ReturnsStream()
  {
    var pipeline = CreatePipeline();
    await pipeline.ConstructAsync(CancellationToken.None);

    var result = await pipeline.SubscribeDataAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(result.AsT0, Is.Not.Null);
    Assert.That(result.AsT0.Info.DataFormat, Is.EqualTo("h264"));

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is not constructed when SubscribeDataAsync is called
  ///
  /// ACTION:
  /// Subscribe to the data stream
  ///
  /// EXPECTED RESULT:
  /// Returns Unavailable error
  /// </summary>
  [Test]
  public async Task SubscribeData_WhenNotConstructed_ReturnsError()
  {
    var pipeline = CreatePipeline();

    var result = await pipeline.SubscribeDataAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is disposed
  ///
  /// ACTION:
  /// Call SubscribeDataAsync
  ///
  /// EXPECTED RESULT:
  /// Returns Unavailable error
  /// </summary>
  [Test]
  public async Task SubscribeData_WhenDisposed_ReturnsError()
  {
    var pipeline = CreatePipeline();
    await pipeline.DisposeAsync();

    var result = await pipeline.SubscribeDataAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is constructed, connection is disposed after construction
  ///
  /// ACTION:
  /// Check connection state after construction
  ///
  /// EXPECTED RESULT:
  /// Connection used for construction is disposed (pipeline disconnects after init)
  /// </summary>
  [Test]
  public async Task Construct_DisposesConnectionAfterInit()
  {
    var connection = new MockStreamConnection();
    var pipeline = CreatePipeline(connection: connection);

    await pipeline.ConstructAsync(CancellationToken.None);

    Assert.That(connection.Disposed, Is.True);
    Assert.That(pipeline.IsConstructed, Is.True);

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is constructed twice
  ///
  /// ACTION:
  /// Call ConstructAsync twice
  ///
  /// EXPECTED RESULT:
  /// Second call returns success without reconnecting
  /// </summary>
  [Test]
  public async Task Construct_WhenAlreadyConstructed_ReturnsSuccess()
  {
    var captureSource = new MockCaptureSource();
    var pipeline = CreatePipeline(captureSource: captureSource);

    await pipeline.ConstructAsync(CancellationToken.None);
    var result = await pipeline.ConstructAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(captureSource.ConnectCount, Is.EqualTo(1));

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is constructed and video subscribe is called without format plugin
  ///
  /// ACTION:
  /// Call SubscribeMuxAsync (no format plugin registered)
  ///
  /// EXPECTED RESULT:
  /// Returns Unavailable error since no video pipeline exists
  /// </summary>
  [Test]
  public async Task SubscribeMux_NoFormatPlugin_ReturnsError()
  {
    var pipeline = CreatePipeline();
    await pipeline.ConstructAsync(CancellationToken.None);

    var result = await pipeline.SubscribeMuxAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is not constructed when SubscribeMuxAsync is called
  ///
  /// ACTION:
  /// Subscribe to the video stream
  ///
  /// EXPECTED RESULT:
  /// Returns Unavailable error
  /// </summary>
  [Test]
  public async Task SubscribeMux_WhenNotConstructed_ReturnsError()
  {
    var pipeline = CreatePipeline();

    var result = await pipeline.SubscribeMuxAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is disposed
  ///
  /// ACTION:
  /// Call SubscribeMuxAsync
  ///
  /// EXPECTED RESULT:
  /// Returns Unavailable error
  /// </summary>
  [Test]
  public async Task SubscribeMux_WhenDisposed_ReturnsError()
  {
    var pipeline = CreatePipeline();
    await pipeline.DisposeAsync();

    var result = await pipeline.SubscribeMuxAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline constructed, MuxInfo is null (no format plugin)
  ///
  /// ACTION:
  /// Read MuxInfo
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public async Task MuxInfo_NoFormatPlugin_ReturnsNull()
  {
    var pipeline = CreatePipeline();
    await pipeline.ConstructAsync(CancellationToken.None);

    Assert.That(pipeline.MuxInfo, Is.Null);

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline constructed, MuxHeader is empty (no format plugin)
  ///
  /// ACTION:
  /// Read MuxHeader
  ///
  /// EXPECTED RESULT:
  /// Returns empty
  /// </summary>
  [Test]
  public async Task MuxHeader_NoFormatPlugin_ReturnsEmpty()
  {
    var pipeline = CreatePipeline();
    await pipeline.ConstructAsync(CancellationToken.None);

    Assert.That(pipeline.MuxHeader.IsEmpty, Is.True);

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// A recorder-like subscriber holds the mux stream while the last data tap leaves
  /// (the dropped-recording bug: preview source switched away, recorder still attached)
  ///
  /// ACTION:
  /// Subscribe mux, subscribe data, then dispose the data subscription
  ///
  /// EXPECTED RESULT:
  /// The source stays connected because the mux subscriber still holds demand
  /// </summary>
  [Test]
  public async Task MuxSubscriberHoldsDemand_WhenLastDataTapLeaves()
  {
    var captureSource = new FreshConnectionCaptureSource();
    var pipeline = CreatePipeline(captureSource: captureSource, withFormat: true);
    pipeline.DisconnectLinger = TimeSpan.FromMilliseconds(50);
    await pipeline.ConstructAsync(CancellationToken.None);

    var muxResult = await pipeline.SubscribeMuxAsync(CancellationToken.None);
    Assert.That(muxResult.IsT0, Is.True);
    await WaitUntilAsync(() => pipeline.IsActive);

    var dataResult = await pipeline.SubscribeDataAsync(CancellationToken.None);
    Assert.That(dataResult.IsT0, Is.True);
    Assert.That(pipeline.GetDemand(), Is.EqualTo(2));

    (dataResult.AsT0 as IDisposable)!.Dispose();

    await Task.Delay(300);
    Assert.That(pipeline.GetDemand(), Is.EqualTo(1));
    Assert.That(pipeline.IsActive, Is.True);

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Demand appears, disappears, and reappears on a constructed pipeline
  ///
  /// ACTION:
  /// Subscribe data, dispose the subscription, subscribe again
  ///
  /// EXPECTED RESULT:
  /// The source connects on demand, disconnects after the linger when demand is gone,
  /// and reconnects when demand returns
  /// </summary>
  [Test]
  public async Task Demand_ConnectsDisconnectsAndReconnectsSource()
  {
    var captureSource = new FreshConnectionCaptureSource();
    var pipeline = CreatePipeline(captureSource: captureSource);
    pipeline.DisconnectLinger = TimeSpan.FromMilliseconds(50);
    await pipeline.ConstructAsync(CancellationToken.None);
    Assert.That(pipeline.IsActive, Is.False);

    var sub1 = await pipeline.SubscribeDataAsync(CancellationToken.None);
    await WaitUntilAsync(() => pipeline.IsActive);

    (sub1.AsT0 as IDisposable)!.Dispose();
    await WaitUntilAsync(() => !pipeline.IsActive);

    var sub2 = await pipeline.SubscribeDataAsync(CancellationToken.None);
    await WaitUntilAsync(() => pipeline.IsActive);

    (sub2.AsT0 as IDisposable)!.Dispose();
    await pipeline.DisposeAsync();
  }

  private static async Task WaitUntilAsync(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    while (!condition())
    {
      if (DateTime.UtcNow > deadline)
        Assert.Fail("Condition not met within timeout");
      await Task.Delay(10);
    }
  }

  private static CameraPipeline CreatePipeline(
    ICaptureSource? captureSource = null,
    MockStreamConnection? connection = null,
    RecordingEventBus? eventBus = null,
    bool withFormat = false)
  {
    var cs = captureSource ?? new MockCaptureSource();
    if (connection != null && cs is MockCaptureSource mock)
      mock.Connection = connection;

    var host = withFormat
      ? new FakePluginHost { StreamFormats = [new FakeFmp4Format()] }
      : new FakePluginHost();

    return new CameraPipeline(
      Guid.NewGuid(), "main", null,
      new CameraConnectionInfo { CameraId = Guid.NewGuid(), Uri = "rtsp://192.168.1.100/stream" },
      cs, host,
      eventBus ?? new RecordingEventBus(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
  }

  private sealed class FreshConnectionCaptureSource : ICaptureSource
  {
    public string Protocol => "rtsp";

    public Task<OneOf<IStreamConnection, Error>> ConnectAsync(
      CameraConnectionInfo info, CancellationToken ct) =>
      Task.FromResult(OneOf<IStreamConnection, Error>.FromT0(new MockStreamConnection()));
  }

  private sealed class FakeFmp4Format : IStreamFormat
  {
    public string FormatId => "fmp4";
    public string FileExtension => "mp4";
    public Type InputType => typeof(H264NalUnit);
    public Type OutputType => typeof(Fmp4Fragment);

    public Task<OneOf<IMuxStream, Error>> CreatePipelineAsync(
      IDataStream input, StreamInfo info, CancellationToken ct) =>
      Task.FromResult(OneOf<IMuxStream, Error>.FromT0(new IdleMuxStream()));

    public OneOf<ISegmentReader, Error> CreateReader(Stream input) =>
      throw new NotImplementedException();

    private sealed class IdleMuxStream : IMuxStream<Fmp4Fragment>
    {
      public MuxStreamInfo Info { get; } = new()
      {
        DataFormat = "fmp4",
        MimeType = "video/mp4",
        FileExtension = "mp4",
        Resolution = "1920x1080",
        Fps = 30
      };
      public ReadOnlyMemory<byte> Header => ReadOnlyMemory<byte>.Empty;
      public Type FrameType => typeof(Fmp4Fragment);
      public Action<MuxStreamStats>? OnStats { set { } }

      public async IAsyncEnumerable<Fmp4Fragment> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
      {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
      }
    }
  }

  private sealed class MockCaptureSource : ICaptureSource
  {
    public string Protocol => "rtsp";
    public int ConnectCount { get; private set; }
    public Error? ConnectError { get; set; }
    public MockStreamConnection Connection { get; set; } = new();

    public Task<OneOf<IStreamConnection, Error>> ConnectAsync(
      CameraConnectionInfo info, CancellationToken ct)
    {
      ConnectCount++;
      if (ConnectError.HasValue)
        return Task.FromResult<OneOf<IStreamConnection, Error>>(ConnectError.Value);

      return Task.FromResult(OneOf<IStreamConnection, Error>.FromT0(Connection));
    }
  }

  private sealed class MockStreamConnection : IStreamConnection
  {
    private readonly TaskCompletionSource _tcs = new();
    private readonly Channel<H264NalUnit> _channel = Channel.CreateUnbounded<H264NalUnit>();

    public StreamInfo Info { get; } = new() { DataFormat = "h264" };
    public IDataStream DataStream { get; }
    public Task Completed => _tcs.Task;
    public bool Disposed { get; private set; }

    public MockStreamConnection()
    {
      DataStream = new MockDataStream(Info, _channel.Reader);
    }

    public void Complete() => _tcs.TrySetResult();
    public void CompleteDataStream() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
      Disposed = true;
      _channel.Writer.TryComplete();
      _tcs.TrySetResult();
      return ValueTask.CompletedTask;
    }
  }

  private sealed class MockDataStream : IDataStream<H264NalUnit>
  {
    private readonly ChannelReader<H264NalUnit> _reader;

    public StreamInfo Info { get; }
    public Type FrameType => typeof(H264NalUnit);

    public MockDataStream(StreamInfo info, ChannelReader<H264NalUnit> reader)
    {
      Info = info;
      _reader = reader;
    }

    public async IAsyncEnumerable<H264NalUnit> ReadAsync(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
      await foreach (var item in _reader.ReadAllAsync(ct))
        yield return item;
    }
  }

  private sealed class RecordingEventBus : IEventBus
  {
    public List<ISystemEvent> Published { get; } = [];

    public Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent
    {
      lock (Published)
        Published.Add(evt);
      return Task.CompletedTask;
    }

    public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct)
      where T : ISystemEvent => Empty<T>();

    private static async IAsyncEnumerable<T> Empty<T>()
    {
      await Task.CompletedTask;
      yield break;
    }
  }
}
