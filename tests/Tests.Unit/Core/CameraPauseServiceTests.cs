using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Server.Core;
using Server.Core.Services;
using Server.Plugins;
using Shared.Models.Events;
using Tests.Unit.Mocks;

namespace Tests.Unit.Core;

[TestFixture]
public class CameraPauseServiceTests
{
  private static readonly DateTimeOffset Start = new(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);
  private static readonly ulong StartUs = Start.ToUnixMicroseconds();
  private const ulong SecondUs = 1_000_000;

  /// <summary>
  /// SCENARIO:
  /// A camera is not paused
  ///
  /// ACTION:
  /// Pause it for one hour
  ///
  /// EXPECTED RESULT:
  /// The end time is one hour after the server's clock, it is persisted, the camera reports
  /// "paused" with that end time, and a single CameraPauseChanged carries it
  /// </summary>
  [Test]
  public async Task Pause_EndTimeComesFromServerClock()
  {
    await using var h = await Harness.StartAsync();

    var result = await h.Service.PauseAsync(h.CameraId, 3600, CancellationToken.None);

    var expected = StartUs + 3600 * SecondUs;
    Assert.That(result.AsT0.PausedUntil, Is.EqualTo(expected));
    Assert.That(h.Config.Values[Harness.Key(h.CameraId)], Is.EqualTo(expected.ToString()));
    Assert.That(h.Status.GetStatus(h.CameraId), Is.EqualTo("paused"));
    Assert.That(h.Status.GetPausedUntil(h.CameraId), Is.EqualTo(expected));
    Assert.That(h.Bus.Published.OfType<CameraPauseChanged>().Select(e => e.PausedUntil),
      Is.EqualTo(new ulong?[] { expected }));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is paused for 15 minutes
  ///
  /// ACTION:
  /// Five minutes in, pause it again for one hour, then advance past the original end
  ///
  /// EXPECTED RESULT:
  /// The new end replaces the old one, measured from the second request, and the original
  /// timer does not resume the camera early
  /// </summary>
  [Test]
  public async Task Pause_AgainReplacesEndFromNow()
  {
    await using var h = await Harness.StartAsync();

    await h.Service.PauseAsync(h.CameraId, 900, CancellationToken.None);
    h.Time.Advance(TimeSpan.FromMinutes(5));
    var result = await h.Service.PauseAsync(h.CameraId, 3600, CancellationToken.None);
    h.Time.Advance(TimeSpan.FromMinutes(11));
    await Task.Delay(100);

    Assert.That(result.AsT0.PausedUntil, Is.EqualTo(StartUs + 300 * SecondUs + 3600 * SecondUs));
    Assert.That(h.Service.IsPaused(h.CameraId), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is paused for ten minutes
  ///
  /// ACTION:
  /// Advance the clock past the end time
  ///
  /// EXPECTED RESULT:
  /// The camera resumes on its own: the stored end time is removed, its status is no longer
  /// "paused", and a CameraPauseChanged with no end time is published
  /// </summary>
  [Test]
  public async Task Expiry_ResumesCamera()
  {
    await using var h = await Harness.StartAsync();

    await h.Service.PauseAsync(h.CameraId, 600, CancellationToken.None);
    h.Time.Advance(TimeSpan.FromSeconds(601));
    await Task.Delay(100);

    Assert.That(h.Service.IsPaused(h.CameraId), Is.False);
    Assert.That(h.Config.Values, Does.Not.ContainKey(Harness.Key(h.CameraId)));
    Assert.That(h.Status.GetStatus(h.CameraId), Is.Not.EqualTo("paused"));
    Assert.That(h.Bus.Published.OfType<CameraPauseChanged>().Select(e => e.PausedUntil),
      Is.EqualTo(new ulong?[] { StartUs + 600 * SecondUs, null }));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is paused
  ///
  /// ACTION:
  /// Pause it with a duration of zero
  ///
  /// EXPECTED RESULT:
  /// The camera resumes immediately and the response carries no end time
  /// </summary>
  [Test]
  public async Task ZeroDuration_ResumesNow()
  {
    await using var h = await Harness.StartAsync();

    await h.Service.PauseAsync(h.CameraId, 600, CancellationToken.None);
    var result = await h.Service.PauseAsync(h.CameraId, 0, CancellationToken.None);

    Assert.That(result.AsT0.PausedUntil, Is.Null);
    Assert.That(h.Service.IsPaused(h.CameraId), Is.False);
    Assert.That(h.Config.Values, Does.Not.ContainKey(Harness.Key(h.CameraId)));
    Assert.That(h.Bus.Published.OfType<CameraPauseChanged>().Select(e => e.PausedUntil),
      Is.EqualTo(new ulong?[] { StartUs + 600 * SecondUs, null }));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is not paused
  ///
  /// ACTION:
  /// Pause it with a duration of zero
  ///
  /// EXPECTED RESULT:
  /// Nothing changed, so nothing is published
  /// </summary>
  [Test]
  public async Task ZeroDuration_WhenNotPaused_PublishesNothing()
  {
    await using var h = await Harness.StartAsync();

    await h.Service.PauseAsync(h.CameraId, 0, CancellationToken.None);

    Assert.That(h.Bus.Published.OfType<CameraPauseChanged>(), Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Stored pauses exist from before a restart: one ends in ten minutes, one ended already
  ///
  /// ACTION:
  /// Start the service, then advance past the remaining pause
  ///
  /// EXPECTED RESULT:
  /// The future pause is restored and still resumes on time; the expired one is deleted
  /// without being treated as paused
  /// </summary>
  [Test]
  public async Task Start_RestoresFuturePauseAndDropsExpired()
  {
    var expiredCamera = Guid.NewGuid();
    await using var h = await Harness.StartAsync(config =>
    {
      config[Harness.Key(Harness.DefaultCameraId)] = (StartUs + 600 * SecondUs).ToString();
      config[Harness.Key(expiredCamera)] = (StartUs - SecondUs).ToString();
    });

    Assert.That(h.Service.IsPaused(h.CameraId), Is.True);
    Assert.That(h.Status.GetStatus(h.CameraId), Is.EqualTo("paused"));
    Assert.That(h.Service.IsPaused(expiredCamera), Is.False);
    Assert.That(h.Config.Values, Does.Not.ContainKey(Harness.Key(expiredCamera)));

    h.Time.Advance(TimeSpan.FromSeconds(601));
    await Task.Delay(100);

    Assert.That(h.Service.IsPaused(h.CameraId), Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// No camera exists with the requested id
  ///
  /// ACTION:
  /// Pause it
  ///
  /// EXPECTED RESULT:
  /// The repository's not-found error is returned and nothing is stored or published
  /// </summary>
  [Test]
  public async Task UnknownCamera_ReturnsRepositoryError()
  {
    await using var h = await Harness.StartAsync();
    var unknown = Guid.NewGuid();

    var result = await h.Service.PauseAsync(unknown, 600, CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.NotFound));
    Assert.That(h.Config.Values, Is.Empty);
    Assert.That(h.Bus.Published.OfType<CameraPauseChanged>(), Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A paused camera is removed
  ///
  /// ACTION:
  /// Publish CameraRemoved on the bus the running service is watching
  ///
  /// EXPECTED RESULT:
  /// The pause and its stored end time are discarded
  /// </summary>
  [Test]
  public async Task CameraRemoved_DiscardsPause()
  {
    await using var h = await Harness.StartAsync();

    await h.Service.PauseAsync(h.CameraId, 600, CancellationToken.None);
    await h.Bus.PublishAsync(new CameraRemoved
    {
      CameraId = h.CameraId,
      Name = "Lounge",
      Timestamp = StartUs
    }, CancellationToken.None);
    await Task.Delay(100);

    Assert.That(h.Service.IsPaused(h.CameraId), Is.False);
    Assert.That(h.Config.Values, Does.Not.ContainKey(Harness.Key(h.CameraId)));
  }

  private sealed class Harness : IAsyncDisposable
  {
    public static readonly Guid DefaultCameraId = Guid.NewGuid();

    public Guid CameraId => DefaultCameraId;
    public FakeTimeProvider Time { get; } = new(Start);
    public FakeConfigRepo Config { get; } = new();
    public RecordingEventBus Bus { get; } = new();
    public CameraStatusTracker Status { get; } = new();
    public CameraPauseService Service { get; }

    private Harness()
    {
      var data = new FakeDataProvider(DefaultCameraId, Config);
      Service = new CameraPauseService(
        new FakePluginHost { DataProvider = data }, Status, Bus, Time,
        NullLogger<CameraPauseService>.Instance);
    }

    public static string Key(Guid cameraId) => $"camera/{cameraId}/pausedUntil";

    public static async Task<Harness> StartAsync(Action<Dictionary<string, string>>? seed = null)
    {
      var harness = new Harness();
      seed?.Invoke(harness.Config.Values);
      await harness.Service.StartAsync(CancellationToken.None);
      await Task.Delay(50);
      return harness;
    }

    public ValueTask DisposeAsync() => Service.DisposeAsync();
  }

  private sealed class RecordingEventBus : IEventBus
  {
    private readonly EventBus _inner = new();
    public List<ISystemEvent> Published { get; } = [];

    public Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent
    {
      lock (Published) Published.Add(evt);
      return _inner.PublishAsync(evt, ct);
    }

    public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct) where T : ISystemEvent =>
      _inner.SubscribeAsync<T>(ct);
  }

  private sealed class FakeDataProvider(Guid cameraId, FakeConfigRepo config) : IDataProvider
  {
    public string ProviderId => "fake";
    public ICameraRepository Cameras { get; } = new SingleCameraRepo(cameraId);
    public IStreamRepository Streams => throw new NotImplementedException();
    public ISegmentRepository Segments => throw new NotImplementedException();
    public IKeyframeRepository Keyframes => throw new NotImplementedException();
    public IEventRepository Events => throw new NotImplementedException();
    public ISystemEventRepository SystemEvents => throw new NotImplementedException();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config => config;
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();
  }

  private sealed class SingleCameraRepo(Guid cameraId) : ICameraRepository
  {
    public Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct = default) =>
      Task.FromResult<OneOf<Camera, Error>>(id == cameraId
        ? new Camera { Id = id, Name = "Lounge", Address = "192.168.1.20", ProviderId = "test" }
        : Error.Create(0, 0, Result.NotFound, $"Camera {id} not found"));

    public Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct = default) =>
      throw new NotImplementedException();
    public Task<OneOf<Camera, Error>> GetByAddressAsync(string address, CancellationToken ct = default) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> CreateAsync(Camera camera, CancellationToken ct = default) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpdateAsync(Camera camera, CancellationToken ct = default) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct = default) =>
      throw new NotImplementedException();
  }

  private sealed class FakeConfigRepo : IConfigRepository
  {
    public Dictionary<string, string> Values { get; } = [];

    public Task<OneOf<string?, Error>> GetAsync(string pluginId, string key, CancellationToken ct = default) =>
      Task.FromResult<OneOf<string?, Error>>(Values.GetValueOrDefault(key));

    public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(
      string pluginId, CancellationToken ct = default) =>
      Task.FromResult<OneOf<IReadOnlyDictionary<string, string>, Error>>(
        new Dictionary<string, string>(Values));

    public Task<OneOf<Success, Error>> SetAsync(
      string pluginId, string key, string value, CancellationToken ct = default)
    {
      Values[key] = value;
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public Task<OneOf<Success, Error>> DeleteAsync(
      string pluginId, string key, CancellationToken ct = default)
    {
      Values.Remove(key);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }
  }
}
