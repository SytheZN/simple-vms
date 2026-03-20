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
  /// Activate the pipeline
  ///
  /// EXPECTED RESULT:
  /// IsActive becomes true, CameraStatusChanged "online" and StreamStarted events published
  /// </summary>
  [Test]
  public async Task Activate_SuccessfulConnect_BecomesActive()
  {
    var eventBus = new RecordingEventBus();
    var pipeline = CreatePipeline(eventBus: eventBus);

    var result = await pipeline.ActivateAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(pipeline.IsActive, Is.True);

    var statusEvent = eventBus.Published.OfType<CameraStatusChanged>().FirstOrDefault();
    Assert.That(statusEvent, Is.Not.Null);
    Assert.That(statusEvent!.Status, Is.EqualTo("online"));

    var streamEvent = eventBus.Published.OfType<StreamStarted>().FirstOrDefault();
    Assert.That(streamEvent, Is.Not.Null);
    Assert.That(streamEvent!.DataFormat, Is.EqualTo("h264"));

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is activated then deactivated
  ///
  /// ACTION:
  /// Deactivate
  ///
  /// EXPECTED RESULT:
  /// IsActive becomes false, connection is disposed
  /// </summary>
  [Test]
  public async Task Deactivate_AfterActivation_BecomesInactive()
  {
    var connection = new MockStreamConnection();
    var pipeline = CreatePipeline(connection: connection);

    await pipeline.ActivateAsync(CancellationToken.None);
    Assert.That(pipeline.IsActive, Is.True);

    await pipeline.DeactivateAsync();
    Assert.That(pipeline.IsActive, Is.False);
    Assert.That(connection.Disposed, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is activated twice
  ///
  /// ACTION:
  /// Call ActivateAsync twice
  ///
  /// EXPECTED RESULT:
  /// Second call returns success without reconnecting
  /// </summary>
  [Test]
  public async Task Activate_WhenAlreadyActive_ReturnsSuccess()
  {
    var captureSource = new MockCaptureSource();
    var pipeline = CreatePipeline(captureSource: captureSource);

    await pipeline.ActivateAsync(CancellationToken.None);
    var result = await pipeline.ActivateAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(captureSource.ConnectCount, Is.EqualTo(1));

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Capture source returns an error on connect
  ///
  /// ACTION:
  /// Activate the pipeline
  ///
  /// EXPECTED RESULT:
  /// Returns the error, IsActive remains false
  /// </summary>
  [Test]
  public async Task Activate_ConnectFails_ReturnsError()
  {
    var captureSource = new MockCaptureSource
    {
      ConnectError = Error.Create(ModuleIds.PluginRtspCapture, 0x01, Result.InternalError, "timeout")
    };
    var pipeline = CreatePipeline(captureSource: captureSource);

    var result = await pipeline.ActivateAsync(CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(pipeline.IsActive, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is activated and a subscriber calls SubscribeDataAsync
  ///
  /// ACTION:
  /// Subscribe to the data stream
  ///
  /// EXPECTED RESULT:
  /// Returns an IDataStream subscriber from the fan-out
  /// </summary>
  [Test]
  public async Task SubscribeData_WhenActive_ReturnsStream()
  {
    var pipeline = CreatePipeline();

    var result = await pipeline.SubscribeDataAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(result.AsT0, Is.Not.Null);
    Assert.That(result.AsT0.Info.DataFormat, Is.EqualTo("h264"));

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline is not active when SubscribeDataAsync is called
  ///
  /// ACTION:
  /// Subscribe to the data stream
  ///
  /// EXPECTED RESULT:
  /// Pipeline activates automatically, then returns a subscriber
  /// </summary>
  [Test]
  public async Task SubscribeData_WhenInactive_ActivatesFirst()
  {
    var captureSource = new MockCaptureSource();
    var pipeline = CreatePipeline(captureSource: captureSource);

    Assert.That(pipeline.IsActive, Is.False);

    var result = await pipeline.SubscribeDataAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(pipeline.IsActive, Is.True);
    Assert.That(captureSource.ConnectCount, Is.EqualTo(1));

    await pipeline.DisposeAsync();
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
  /// Pipeline activated, connection's Completed task finishes (simulating disconnect)
  ///
  /// ACTION:
  /// Complete the mock connection
  ///
  /// EXPECTED RESULT:
  /// Pipeline publishes offline status and StreamStopped events
  /// </summary>
  [Test]
  public async Task ConnectionDrop_PublishesOfflineEvents()
  {
    var eventBus = new RecordingEventBus();
    var connection = new MockStreamConnection();
    var pipeline = CreatePipeline(connection: connection, eventBus: eventBus);

    await pipeline.ActivateAsync(CancellationToken.None);
    eventBus.Published.Clear();

    connection.Complete();
    await Task.Delay(100);

    var statusEvent = eventBus.Published.OfType<CameraStatusChanged>()
      .FirstOrDefault(e => e.Status == "offline");
    Assert.That(statusEvent, Is.Not.Null);

    var stopEvent = eventBus.Published.OfType<StreamStopped>().FirstOrDefault();
    Assert.That(stopEvent, Is.Not.Null);

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline activated, connection drops, capture source succeeds on reconnect
  ///
  /// ACTION:
  /// Complete the mock connection, then allow reconnect
  ///
  /// EXPECTED RESULT:
  /// Pipeline reconnects and publishes online status again
  /// </summary>
  [Test]
  public async Task ConnectionDrop_Reconnects()
  {
    var eventBus = new RecordingEventBus();
    var captureSource = new MockCaptureSource();
    var pipeline = CreatePipeline(captureSource: captureSource, eventBus: eventBus);

    await pipeline.ActivateAsync(CancellationToken.None);
    Assert.That(captureSource.ConnectCount, Is.EqualTo(1));

    var firstConnection = captureSource.Connection;
    captureSource.Connection = new MockStreamConnection();
    firstConnection.Complete();

    await Task.Delay(2000);

    Assert.That(captureSource.ConnectCount, Is.GreaterThan(1));
    Assert.That(pipeline.IsActive, Is.True);

    var onlineEvents = eventBus.Published.OfType<CameraStatusChanged>()
      .Where(e => e.Status == "online").ToList();
    Assert.That(onlineEvents, Has.Count.GreaterThanOrEqualTo(2));

    await pipeline.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Pipeline activated, subscriber reads then finishes (fan-out becomes empty)
  ///
  /// ACTION:
  /// Subscribe, read, let the subscription's ReadAsync complete
  ///
  /// EXPECTED RESULT:
  /// OnFanOutEmpty fires and pipeline deactivates
  /// </summary>
  [Test]
  public async Task FanOutEmpty_DeactivatesPipeline()
  {
    var connection = new MockStreamConnection();
    var pipeline = CreatePipeline(connection: connection);

    var subResult = await pipeline.SubscribeDataAsync(CancellationToken.None);
    Assert.That(subResult.IsT0, Is.True);
    Assert.That(pipeline.IsActive, Is.True);

    connection.CompleteDataStream();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    try
    {
      await foreach (var _ in ((IDataStream<Shared.Models.Formats.H264NalUnit>)subResult.AsT0)
        .ReadAsync(cts.Token)) { }
    }
    catch (OperationCanceledException) { }

    await Task.Delay(200);

    Assert.That(pipeline.IsActive, Is.False);

    await pipeline.DisposeAsync();
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
