using System.Threading.Channels;
using Server.Plugins;
using Server.Streaming;
using Shared.Models;
using Shared.Models.Events;
using Shared.Models.Formats;

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
  /// Call SubscribeVideoAsync (no format plugin registered)
  ///
  /// EXPECTED RESULT:
  /// Returns Unavailable error since no video pipeline exists
  /// </summary>
  [Test]
  public async Task SubscribeVideo_NoFormatPlugin_ReturnsError()
  {
    var pipeline = CreatePipeline();
    await pipeline.ConstructAsync(CancellationToken.None);

    var result = await pipeline.SubscribeVideoAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is not constructed when SubscribeVideoAsync is called
  ///
  /// ACTION:
  /// Subscribe to the video stream
  ///
  /// EXPECTED RESULT:
  /// Returns Unavailable error
  /// </summary>
  [Test]
  public async Task SubscribeVideo_WhenNotConstructed_ReturnsError()
  {
    var pipeline = CreatePipeline();

    var result = await pipeline.SubscribeVideoAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is disposed
  ///
  /// ACTION:
  /// Call SubscribeVideoAsync
  ///
  /// EXPECTED RESULT:
  /// Returns Unavailable error
  /// </summary>
  [Test]
  public async Task SubscribeVideo_WhenDisposed_ReturnsError()
  {
    var pipeline = CreatePipeline();
    await pipeline.DisposeAsync();

    var result = await pipeline.SubscribeVideoAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline constructed, VideoInfo is null (no format plugin)
  ///
  /// ACTION:
  /// Read VideoInfo
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public async Task VideoInfo_NoFormatPlugin_ReturnsNull()
  {
    var pipeline = CreatePipeline();
    await pipeline.ConstructAsync(CancellationToken.None);

    Assert.That(pipeline.VideoInfo, Is.Null);

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline constructed, VideoHeader is empty (no format plugin)
  ///
  /// ACTION:
  /// Read VideoHeader
  ///
  /// EXPECTED RESULT:
  /// Returns empty
  /// </summary>
  [Test]
  public async Task VideoHeader_NoFormatPlugin_ReturnsEmpty()
  {
    var pipeline = CreatePipeline();
    await pipeline.ConstructAsync(CancellationToken.None);

    Assert.That(pipeline.VideoHeader.IsEmpty, Is.True);

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// OnParameterMismatch callback is set
  ///
  /// ACTION:
  /// Read the property
  ///
  /// EXPECTED RESULT:
  /// Can be set and retrieved
  /// </summary>
  [Test]
  public void OnParameterMismatch_CanBeSet()
  {
    var pipeline = CreatePipeline();
    var called = false;
    pipeline.OnParameterMismatch = () => called = true;
    pipeline.OnParameterMismatch!.Invoke();
    Assert.That(called, Is.True);
  }

  private static CameraPipeline CreatePipeline(
    MockCaptureSource? captureSource = null,
    MockStreamConnection? connection = null,
    RecordingEventBus? eventBus = null)
  {
    var cs = captureSource ?? new MockCaptureSource();
    if (connection != null)
      cs.Connection = connection;

    return new CameraPipeline(
      Guid.NewGuid(), "main",
      new CameraConnectionInfo { Uri = "rtsp://192.168.1.100/stream" },
      cs, new FakePluginHost(),
      eventBus ?? new RecordingEventBus(),
      Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
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
