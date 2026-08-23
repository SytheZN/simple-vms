using Microsoft.Extensions.Logging.Abstractions;
using Server.Core;
using Server.Plugins;
using Shared.Models.Events;
using Tests.Unit.Mocks;

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
    var host = new FakePluginHost { DataProvider = data, EventFilters = [new PassFilter()] };
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
    var host = new FakePluginHost { DataProvider = data, EventFilters = [new SuppressFilter()] };
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
    var host = new FakePluginHost { DataProvider = data };
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 5000,
      Metadata = new Dictionary<string, string> { ["State"] = "true", ["topic"] = "motion" }
    };

    await manager.ProcessEventAsync(evt, CancellationToken.None);

    Assert.That(eventBus.Published.OfType<MotionDetected>().Count(), Is.EqualTo(1));
    Assert.That(eventBus.Published.OfType<MotionDetected>().First().CameraId, Is.EqualTo(evt.CameraId));
  }

  /// <summary>
  /// SCENARIO:
  /// A motion event reporting State=false
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
    var host = new FakePluginHost { DataProvider = data };
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 6000,
      Metadata = new Dictionary<string, string> { ["State"] = "false", ["topic"] = "motion" }
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
    var host = new FakePluginHost { DataProvider = data, NotificationSinks = [sink] };
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

  /// <summary>
  /// SCENARIO:
  /// A camera event is persisted to history
  ///
  /// ACTION:
  /// Call ProcessEventAsync with the event
  ///
  /// EXPECTED RESULT:
  /// CameraEventRecorded carries the row as written, identifier included, so a client shown it
  /// finds the same event when it queries
  /// </summary>
  [Test]
  public async Task ProcessEvent_Persisted_PublishesTheRow()
  {
    var data = new FakeDataProvider();
    var eventBus = new FakeEventBus();
    var host = new FakePluginHost { DataProvider = data };
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "tamper",
      StartTime = 1000
    };

    await manager.ProcessEventAsync(evt, CancellationToken.None);

    var recorded = eventBus.Published.OfType<CameraEventRecorded>().ToList();
    Assert.That(recorded, Has.Count.EqualTo(1));
    Assert.That(recorded[0].Id, Is.EqualTo(evt.Id));
    Assert.That(recorded[0].Type, Is.EqualTo("tamper"));
    Assert.That(recorded[0].Ended, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// A motion event starts and later stops on the same camera
  ///
  /// ACTION:
  /// Call ProcessEventAsync with State=true, then again with State=false
  ///
  /// EXPECTED RESULT:
  /// One row is written and then updated with the end time; the second publish carries the
  /// same identifier flagged as ended, rather than a second row
  /// </summary>
  [Test]
  public async Task ProcessEvent_MotionStartThenStop_ClosesTheSameRow()
  {
    var data = new FakeDataProvider();
    var eventBus = new FakeEventBus();
    var host = new FakePluginHost { DataProvider = data };
    var manager = new EventManager(host, eventBus, NullLogger.Instance);
    var cameraId = Guid.NewGuid();
    var startId = Guid.NewGuid();

    await manager.ProcessEventAsync(new CameraEvent
    {
      Id = startId,
      CameraId = cameraId,
      Type = "motion",
      StartTime = 6000,
      Metadata = new Dictionary<string, string> { ["State"] = "true" }
    }, CancellationToken.None);

    await manager.ProcessEventAsync(new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = cameraId,
      Type = "motion",
      StartTime = 9000,
      Metadata = new Dictionary<string, string> { ["State"] = "false" }
    }, CancellationToken.None);

    var repo = (FakeEventRepo)data.Events;
    Assert.That(repo.Created, Has.Count.EqualTo(1));
    Assert.That(repo.Updated, Has.Count.EqualTo(1));
    Assert.That(repo.Updated[0].Id, Is.EqualTo(startId));
    Assert.That(repo.Updated[0].EndTime, Is.EqualTo(9000));

    var recorded = eventBus.Published.OfType<CameraEventRecorded>().ToList();
    Assert.That(recorded, Has.Count.EqualTo(2));
    Assert.That(recorded[0].Ended, Is.False);
    Assert.That(recorded[1].Id, Is.EqualTo(startId));
    Assert.That(recorded[1].Ended, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera reports a property as inactive with no matching start, as happens in the state
  /// snapshot sent on every subscription
  ///
  /// ACTION:
  /// Call ProcessEventAsync with State=false and nothing open for that camera and type
  ///
  /// EXPECTED RESULT:
  /// Nothing is written to history and nothing is published; an end that closes nothing is
  /// not an event
  /// </summary>
  [Test]
  public async Task ProcessEvent_InactiveWithNothingOpen_IsIgnored()
  {
    var data = new FakeDataProvider();
    var eventBus = new FakeEventBus();
    var host = new FakePluginHost { DataProvider = data };
    var manager = new EventManager(host, eventBus, NullLogger.Instance);

    await manager.ProcessEventAsync(new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 6000,
      Metadata = new Dictionary<string, string> { ["State"] = "false" }
    }, CancellationToken.None);

    var repo = (FakeEventRepo)data.Events;
    Assert.That(repo.Created, Is.Empty);
    Assert.That(repo.Updated, Is.Empty);
    Assert.That(eventBus.Published.OfType<CameraEventRecorded>(), Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is added
  ///
  /// ACTION:
  /// Publish CameraAdded on the bus the running manager is watching
  ///
  /// EXPECTED RESULT:
  /// An "added" event is written to history
  /// </summary>
  [Test]
  public async Task CameraAdded_RecordsAdded()
  {
    var data = new FakeDataProvider();
    var eventBus = new EventBus();
    var cameraId = Guid.NewGuid();

    await using var manager = await StartManagerAsync(data, eventBus);

    await eventBus.PublishAsync(new CameraAdded
    {
      CameraId = cameraId,
      Timestamp = 2_000_000
    }, CancellationToken.None);
    await Task.Delay(100);

    Assert.That(data.CreatedEvents, Has.Count.EqualTo(1));
    Assert.That(data.CreatedEvents[0].Type, Is.EqualTo("added"));
    Assert.That(data.CreatedEvents[0].CameraId, Is.EqualTo(cameraId));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera drops and its pipeline reports offline with reason "disconnected"
  ///
  /// ACTION:
  /// Publish CameraStatusChanged on the bus the running manager is watching
  ///
  /// EXPECTED RESULT:
  /// A "disconnect" event is written to history naming the profile
  /// </summary>
  [Test]
  public async Task StatusChanged_Disconnected_RecordsDisconnect()
  {
    var data = new FakeDataProvider();
    var eventBus = new EventBus();
    var cameraId = Guid.NewGuid();

    await using var manager = await StartManagerAsync(data, eventBus);

    await PublishStatusAsync(eventBus, cameraId, "offline", "disconnected");

    Assert.That(data.CreatedEvents, Has.Count.EqualTo(1));
    Assert.That(data.CreatedEvents[0].Type, Is.EqualTo("disconnect"));
    Assert.That(data.CreatedEvents[0].CameraId, Is.EqualTo(cameraId));
    Assert.That(data.CreatedEvents[0].Metadata!["profile"], Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// The last consumer of a stream goes away, so the pipeline reports offline with "no demand"
  ///
  /// ACTION:
  /// Publish CameraStatusChanged on the bus the running manager is watching
  ///
  /// EXPECTED RESULT:
  /// Nothing is written, because the camera did not go anywhere
  /// </summary>
  [Test]
  public async Task StatusChanged_NoDemand_IsNotRecorded()
  {
    var data = new FakeDataProvider();
    var eventBus = new EventBus();

    await using var manager = await StartManagerAsync(data, eventBus);

    await PublishStatusAsync(eventBus, Guid.NewGuid(), "offline", "no demand");

    Assert.That(data.CreatedEvents, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera that was recorded as disconnected comes back
  ///
  /// ACTION:
  /// Publish a disconnected status followed by an online status
  ///
  /// EXPECTED RESULT:
  /// The recovery is recorded as "connect" after the "disconnect"
  /// </summary>
  [Test]
  public async Task StatusChanged_OnlineAfterDisconnect_RecordsConnect()
  {
    var data = new FakeDataProvider();
    var eventBus = new EventBus();
    var cameraId = Guid.NewGuid();

    await using var manager = await StartManagerAsync(data, eventBus);

    await PublishStatusAsync(eventBus, cameraId, "offline", "disconnected");
    await PublishStatusAsync(eventBus, cameraId, "online", null);

    Assert.That(data.CreatedEvents.Select(e => e.Type),
      Is.EqualTo(new[] { "disconnect", "connect" }));
  }

  /// <summary>
  /// SCENARIO:
  /// A pipeline connects because a viewer arrived, with no preceding disconnect
  ///
  /// ACTION:
  /// Publish an online status on the bus the running manager is watching
  ///
  /// EXPECTED RESULT:
  /// Nothing is written, because a connect only means something after a drop
  /// </summary>
  [Test]
  public async Task StatusChanged_OnlineWithoutDisconnect_IsNotRecorded()
  {
    var data = new FakeDataProvider();
    var eventBus = new EventBus();

    await using var manager = await StartManagerAsync(data, eventBus);

    await PublishStatusAsync(eventBus, Guid.NewGuid(), "online", null);

    Assert.That(data.CreatedEvents, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is reconfigured
  ///
  /// ACTION:
  /// Publish CameraConfigChanged on the bus the running manager is watching
  ///
  /// EXPECTED RESULT:
  /// A "config" event is written to history
  /// </summary>
  [Test]
  public async Task ConfigChanged_RecordsConfig()
  {
    var data = new FakeDataProvider();
    var eventBus = new EventBus();
    var cameraId = Guid.NewGuid();

    await using var manager = await StartManagerAsync(data, eventBus);

    await eventBus.PublishAsync(new CameraConfigChanged
    {
      CameraId = cameraId,
      Timestamp = 4_000_000
    }, CancellationToken.None);
    await Task.Delay(100);

    Assert.That(data.CreatedEvents, Has.Count.EqualTo(1));
    Assert.That(data.CreatedEvents[0].Type, Is.EqualTo("config"));
    Assert.That(data.CreatedEvents[0].CameraId, Is.EqualTo(cameraId));
  }

  private static async Task<EventManager> StartManagerAsync(
    FakeDataProvider data, IEventBus eventBus)
  {
    var manager = new EventManager(
      new FakePluginHost { DataProvider = data }, eventBus, NullLogger.Instance);
    await manager.StartAsync(CancellationToken.None);
    await Task.Delay(50);
    return manager;
  }

  private static async Task PublishStatusAsync(
    IEventBus eventBus, Guid cameraId, string status, string? reason)
  {
    await eventBus.PublishAsync(new CameraStatusChanged
    {
      CameraId = cameraId,
      Profile = "main",
      Status = status,
      Reason = reason,
      Timestamp = 3_000_000
    }, CancellationToken.None);
    await Task.Delay(100);
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
    public ICameraRepository Cameras { get; } = new EmptyCameraRepo();
    public IStreamRepository Streams => throw new NotImplementedException();
    public ISegmentRepository Segments => throw new NotImplementedException();
    public IKeyframeRepository Keyframes => throw new NotImplementedException();
    public IEventRepository Events { get; } = new FakeEventRepo();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config => throw new NotImplementedException();
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();

    public List<CameraEvent> CreatedEvents => ((FakeEventRepo)Events).Created;
  }

  private sealed class EmptyCameraRepo : ICameraRepository
  {
    public Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct = default) =>
      Task.FromResult<OneOf<IReadOnlyList<Camera>, Error>>(Array.Empty<Camera>());

    public Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct = default) =>
      Task.FromResult<OneOf<Camera, Error>>(
        Error.Create(0, 0, Result.NotFound, $"Camera {id} not found"));
    public Task<OneOf<Camera, Error>> GetByAddressAsync(string address, CancellationToken ct = default) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> CreateAsync(Camera camera, CancellationToken ct = default) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpdateAsync(Camera camera, CancellationToken ct = default) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct = default) =>
      throw new NotImplementedException();
  }

  private sealed class FakeEventRepo : IEventRepository
  {
    public List<CameraEvent> Created { get; } = [];

    public Task<OneOf<Success, Error>> CreateAsync(CameraEvent evt, CancellationToken ct)
    {
      Created.Add(evt);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public List<CameraEvent> Updated { get; } = [];

    public Task<OneOf<Success, Error>> UpdateAsync(CameraEvent evt, CancellationToken ct)
    {
      Updated.Add(evt);
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
    public Task<OneOf<int, Error>> DeleteOlderThanAsync(
      Guid cameraId, ulong cutoff, CancellationToken ct) =>
      throw new NotImplementedException();
  }

}
