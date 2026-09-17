using Microsoft.Extensions.Logging.Abstractions;
using Server.Core;
using Server.Core.Services;
using Server.Plugins;
using Shared.Models.Events;
using Tests.Unit.Mocks;

namespace Tests.Unit.Core;

[TestFixture]
public class CameraServiceReprobeTests
{
  /// <summary>
  /// SCENARIO:
  /// A reprobe of a camera is still running when another reprobe of the same camera is
  /// requested, and a third is requested after the first has finished
  ///
  /// ACTION:
  /// Publish three CameraReprobeRequested events around a reprobe that is held open
  ///
  /// EXPECTED RESULT:
  /// The overlapping request is dropped and the later one runs, so the camera is looked up
  /// exactly twice
  /// </summary>
  [Test]
  public async Task ReprobeRequested_DropsOverlappingRequestAndRunsLaterOne()
  {
    var cameras = new HeldCameraLookups();
    var eventBus = new EventBus();
    var service = new CameraService(
      new FakePluginHost { DataProvider = new CamerasOnlyDataProvider(cameras) },
      new CameraStatusTracker(), eventBus, NullLogger<CameraService>.Instance);
    await service.StartAsync(CancellationToken.None);
    await Task.Delay(50);
    var cameraId = Guid.NewGuid();

    await RequestReprobeAsync(eventBus, cameraId);
    await cameras.WaitForLookupsAsync(1);

    await RequestReprobeAsync(eventBus, cameraId);
    await Task.Delay(100);
    Assert.That(cameras.Lookups, Is.EqualTo(1));

    cameras.ReleaseHeldLookups();
    await Task.Delay(100);

    await RequestReprobeAsync(eventBus, cameraId);
    await cameras.WaitForLookupsAsync(2);

    await service.StopAsync(CancellationToken.None);
  }

  private static Task RequestReprobeAsync(IEventBus eventBus, Guid cameraId) =>
    eventBus.PublishAsync(new CameraReprobeRequested
    {
      CameraId = cameraId,
      Initiator = "test",
      Timestamp = 0
    }, CancellationToken.None);

  private sealed class HeldCameraLookups : ICameraRepository
  {
    private readonly TaskCompletionSource _released =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _lookups;

    public int Lookups => Volatile.Read(ref _lookups);

    public void ReleaseHeldLookups() => _released.TrySetResult();

    public async Task WaitForLookupsAsync(int expected)
    {
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      while (Lookups < expected)
        await Task.Delay(10, timeout.Token);
    }

    public async Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
      Interlocked.Increment(ref _lookups);
      await _released.Task;
      return Error.Create(ModuleIds.CameraManagement, 0x00FF, Result.NotFound, "no such camera");
    }

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

  private sealed class CamerasOnlyDataProvider(ICameraRepository cameras) : IDataProvider
  {
    public string ProviderId => "fake";
    public ICameraRepository Cameras => cameras;
    public IStreamRepository Streams => throw new NotImplementedException();
    public ISegmentRepository Segments => throw new NotImplementedException();
    public IKeyframeRepository Keyframes => throw new NotImplementedException();
    public IEventRepository Events => throw new NotImplementedException();
    public ISystemEventRepository SystemEvents => throw new NotImplementedException();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config => throw new NotImplementedException();
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();
  }
}
