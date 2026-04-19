using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Events;

namespace Tests.Unit.Core;

[TestFixture]
public class EventManagerTests
{
  /// <summary>
  /// SCENARIO:
  /// A camera event passes through all filters without suppression
  ///
  /// ACTION:
  /// Call ProcessEventAsync with the event
  ///
  /// EXPECTED RESULT:
  /// Event is persisted via IEventRepository and OnvifEvent is published on the bus
  /// </summary>
  [Test]
  public async Task ProcessEvent_PassesFilter_PersistsAndPublishes()
  {
    var data = new FakeDataProvider();
    var eventBus = new FakeEventBus();
    var host = new FakePluginHost(data, eventFilters: [new PassFilter()]);
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "tamper",
      StartTime = 1000,
      Metadata = new Dictionary<string, string> { ["topic"] = "tns1:VideoSource/Tamper" }
    };

    await manager.ProcessEventAsync(evt, CancellationToken.None);

    Assert.That(data.CreatedEvents, Has.Count.EqualTo(1));
    Assert.That(data.CreatedEvents[0].Type, Is.EqualTo("tamper"));
    Assert.That(eventBus.Published.OfType<OnvifEvent>().Count(), Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera event is suppressed by a filter
  ///
  /// ACTION:
  /// Call ProcessEventAsync with the event
  ///
  /// EXPECTED RESULT:
  /// Event is NOT persisted and no system events are published
  /// </summary>
  [Test]
  public async Task ProcessEvent_FilterSuppresses_NotPersisted()
  {
    var data = new FakeDataProvider();
    var eventBus = new FakeEventBus();
    var host = new FakePluginHost(data, eventFilters: [new SuppressFilter()]);
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 1000
    };

    await manager.ProcessEventAsync(evt, CancellationToken.None);

    Assert.That(data.CreatedEvents, Is.Empty);
    Assert.That(eventBus.Published, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A motion event with active=True metadata
  ///
  /// ACTION:
  /// Call ProcessEventAsync
  ///
  /// EXPECTED RESULT:
  /// MotionDetected is published on the event bus
  /// </summary>
  [Test]
  public async Task ProcessEvent_MotionActive_PublishesMotionDetected()
  {
    var data = new FakeDataProvider();
    var eventBus = new FakeEventBus();
    var host = new FakePluginHost(data);
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 5000,
      Metadata = new Dictionary<string, string> { ["active"] = "True", ["topic"] = "motion" }
    };

    await manager.ProcessEventAsync(evt, CancellationToken.None);

    Assert.That(eventBus.Published.OfType<MotionDetected>().Count(), Is.EqualTo(1));
    Assert.That(eventBus.Published.OfType<MotionDetected>().First().CameraId, Is.EqualTo(evt.CameraId));
  }

  /// <summary>
  /// SCENARIO:
  /// A motion event with active=False metadata
  ///
  /// ACTION:
  /// Call ProcessEventAsync
  ///
  /// EXPECTED RESULT:
  /// MotionEnded is published on the event bus
  /// </summary>
  [Test]
  public async Task ProcessEvent_MotionInactive_PublishesMotionEnded()
  {
    var data = new FakeDataProvider();
    var eventBus = new FakeEventBus();
    var host = new FakePluginHost(data);
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 6000,
      Metadata = new Dictionary<string, string> { ["active"] = "False", ["topic"] = "motion" }
    };

    await manager.ProcessEventAsync(evt, CancellationToken.None);

    Assert.That(eventBus.Published.OfType<MotionEnded>().Count(), Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A notification sink is registered
  ///
  /// ACTION:
  /// Process an event that passes filters
  ///
  /// EXPECTED RESULT:
  /// The notification sink receives the event
  /// </summary>
  [Test]
  public async Task ProcessEvent_NotifiesSinks()
  {
    var data = new FakeDataProvider();
    var eventBus = new FakeEventBus();
    var sink = new FakeNotificationSink();
    var host = new FakePluginHost(data, notificationSinks: [sink]);
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "io",
      StartTime = 1000
    };

    await manager.ProcessEventAsync(evt, CancellationToken.None);

    Assert.That(sink.SentEvents, Has.Count.EqualTo(1));
  }

  private sealed class PassFilter : IEventFilter
  {
    public string FilterId => "pass";
    public Task<EventFilterResult> ProcessAsync(CameraEvent rawEvent, CancellationToken ct) =>
      Task.FromResult(new EventFilterResult { Decision = EventDecision.Pass });
  }

  private sealed class SuppressFilter : IEventFilter
  {
    public string FilterId => "suppress";
    public Task<EventFilterResult> ProcessAsync(CameraEvent rawEvent, CancellationToken ct) =>
      Task.FromResult(new EventFilterResult { Decision = EventDecision.Suppress });
  }

  private sealed class FakeNotificationSink : INotificationSink
  {
    public string SinkId => "fake";
    public List<CameraEvent> SentEvents { get; } = [];
    public Task SendAsync(CameraEvent evt, CancellationToken ct)
    {
      SentEvents.Add(evt);
      return Task.CompletedTask;
    }
  }

  private sealed class FakeEventBus : IEventBus
  {
    public List<ISystemEvent> Published { get; } = [];
    public Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent
    {
      Published.Add(evt);
      return Task.CompletedTask;
    }
    public async IAsyncEnumerable<T> SubscribeAsync<T>(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
      where T : ISystemEvent
    {
      await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
      yield break;
    }
  }

  private sealed class FakeDataProvider : IDataProvider
  {
    public string ProviderId => "fake";
    public ICameraRepository Cameras => throw new NotImplementedException();
    public IStreamRepository Streams => throw new NotImplementedException();
    public ISegmentRepository Segments => throw new NotImplementedException();
    public IKeyframeRepository Keyframes => throw new NotImplementedException();
    public IEventRepository Events { get; } = new FakeEventRepo();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config => throw new NotImplementedException();
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();

    public List<CameraEvent> CreatedEvents => ((FakeEventRepo)Events).Created;
  }

  private sealed class FakeEventRepo : IEventRepository
  {
    public List<CameraEvent> Created { get; } = [];

    public Task<OneOf<Success, Error>> CreateAsync(CameraEvent evt, CancellationToken ct)
    {
      Created.Add(evt);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public Task<OneOf<IReadOnlyList<CameraEvent>, Error>> QueryAsync(
      Guid? cameraId, string? type, ulong from, ulong to, int limit, int offset, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<CameraEvent, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<IReadOnlyList<CameraEvent>, Error>> GetByTimeRangeAsync(
      Guid cameraId, ulong from, ulong to, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakePluginHost : IPluginHost
  {
    private readonly IDataProvider _data;

    public FakePluginHost(
      IDataProvider data,
      IReadOnlyList<IEventFilter>? eventFilters = null,
      IReadOnlyList<INotificationSink>? notificationSinks = null)
    {
      _data = data;
      EventFilters = eventFilters ?? [];
      NotificationSinks = notificationSinks ?? [];
    }

    public IReadOnlyList<PluginEntry> Plugins => [];
    public IDataProvider DataProvider => _data;
    public IReadOnlyList<ICaptureSource> CaptureSources => [];
    public IReadOnlyList<IStreamFormat> StreamFormats => [];
    public IReadOnlyList<ICameraProvider> CameraProviders => [];
    public IReadOnlyList<IEventFilter> EventFilters { get; }
    public IReadOnlyList<INotificationSink> NotificationSinks { get; }
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
}
