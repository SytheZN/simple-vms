using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class GalleryViewModelExtraTests
{
  /// <summary>
  /// SCENARIO:
  /// LoadAsync is called while the tunnel is disconnected
  ///
  /// ACTION:
  /// Construct VM with a Disconnected tunnel, call LoadAsync
  ///
  /// EXPECTED RESULT:
  /// API is not hit; Cameras stays empty; IsLoading stays false
  /// (early return when disconnected)
  /// </summary>
  [Test]
  public async Task Load_TunnelDisconnected_NoApiCall()
  {
    var api = new GalleryApi { CameraList = [MakeCamera("Cam", "1.2.3.4", "online")] };
    var tunnel = new FakeStreamTunnel { State = ConnectionState.Disconnected };
    var vm = new GalleryViewModel(api, tunnel, new FakeEventService(),
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.Cameras, Is.Empty);
      Assert.That(api.GetCallCount, Is.Zero);
      Assert.That(vm.IsLoading, Is.False);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// API returns an error
  ///
  /// ACTION:
  /// Configure GalleryApi to return Error, LoadAsync
  ///
  /// EXPECTED RESULT:
  /// ErrorMessage is set; Cameras unchanged; IsLoading clears
  /// </summary>
  [Test]
  public async Task Load_ApiError_SetsErrorAndClearsLoading()
  {
    var api = new GalleryApi { ReturnError = true };
    var tunnel = new FakeStreamTunnel();
    var vm = new GalleryViewModel(api, tunnel, new FakeEventService(),
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.ErrorMessage, Is.EqualTo("api fail"));
      Assert.That(vm.Cameras, Is.Empty);
      Assert.That(vm.IsLoading, Is.False);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// SelectedCamera is set
  ///
  /// ACTION:
  /// Subscribe to PropertyChanged, set SelectedCamera
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged fires for SelectedCamera; getter returns the assigned value
  /// </summary>
  [Test]
  public void SelectedCamera_FiresPropertyChanged()
  {
    var vm = new GalleryViewModel(new GalleryApi(), new FakeStreamTunnel(),
      new FakeEventService(), NullLogger<GalleryViewModel>.Instance);
    var changed = new List<string>();
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

    var cam = MakeCamera("Selected", "1.1.1.1", "online");
    vm.SelectedCamera = cam;

    Assert.Multiple(() =>
    {
      Assert.That(vm.SelectedCamera, Is.SameAs(cam));
      Assert.That(changed, Does.Contain(nameof(GalleryViewModel.SelectedCamera)));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Event service emits an event with the Start flag
  ///
  /// ACTION:
  /// Subscribe to CameraEventReceived, fire an event with Start flag
  ///
  /// EXPECTED RESULT:
  /// CameraEventReceived fires with the camera id from the message
  /// </summary>
  [Test]
  public async Task EventStart_RaisesCameraEventReceived()
  {
    var events = new FakeEventService();
    var vm = new GalleryViewModel(new GalleryApi(), new FakeStreamTunnel(),
      events, NullLogger<GalleryViewModel>.Instance);

    Guid? observed = null;
    vm.CameraEventReceived += id => observed = id;

    var cameraId = Guid.NewGuid();
    events.Fire(new EventChannelMessage
    {
      Id = Guid.NewGuid(), CameraId = cameraId, Type = "motion", StartTime = 0
    }, EventChannelFlags.Start);

    await Task.Delay(50);

    Assert.That(observed, Is.EqualTo(cameraId));
  }

  /// <summary>
  /// SCENARIO:
  /// Event service emits an event WITHOUT the Start flag (e.g. End or other)
  ///
  /// ACTION:
  /// Subscribe to CameraEventReceived, fire an event with flags = None
  ///
  /// EXPECTED RESULT:
  /// CameraEventReceived does not fire (only Start triggers UI flash)
  /// </summary>
  [Test]
  public async Task EventWithoutStartFlag_DoesNotRaise()
  {
    var events = new FakeEventService();
    var vm = new GalleryViewModel(new GalleryApi(), new FakeStreamTunnel(),
      events, NullLogger<GalleryViewModel>.Instance);

    var fired = false;
    vm.CameraEventReceived += _ => fired = true;

    events.Fire(new EventChannelMessage
    {
      Id = Guid.NewGuid(), CameraId = Guid.NewGuid(), Type = "motion", StartTime = 0
    }, EventChannelFlags.None);

    await Task.Delay(50);

    Assert.That(fired, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Dispose detaches both tunnel and event-service handlers
  ///
  /// ACTION:
  /// Dispose, then fire StateChanged and an event
  ///
  /// EXPECTED RESULT:
  /// Neither LoadAsync nor CameraEventReceived runs (handlers were unsubscribed)
  /// </summary>
  [Test]
  public async Task Dispose_DetachesHandlers()
  {
    var api = new GalleryApi { CameraList = [MakeCamera("X", "1.2.3.4", "online")] };
    var events = new FakeEventService();
    var tunnel = new FakeStreamTunnel();
    var vm = new GalleryViewModel(api, tunnel, events, NullLogger<GalleryViewModel>.Instance);

    var fired = false;
    vm.CameraEventReceived += _ => fired = true;

    vm.Dispose();
    tunnel.FireStateChanged(ConnectionState.Connected);
    events.Fire(new EventChannelMessage
    {
      Id = Guid.NewGuid(), CameraId = Guid.NewGuid(), Type = "motion", StartTime = 0
    }, EventChannelFlags.Start);
    await Task.Delay(50);

    Assert.Multiple(() =>
    {
      Assert.That(api.GetCallCount, Is.Zero);
      Assert.That(fired, Is.False);
    });
  }

  private static CameraListItem MakeCamera(string name, string address, string status) => new()
  {
    Id = Guid.NewGuid(), Name = name, Address = address,
    Status = status, ProviderId = "onvif", Streams = [], Capabilities = []
  };

  private sealed class GalleryApi : FakeApiClient
  {
    public List<CameraListItem>? CameraList { get; set; }
    public bool ReturnError { get; set; }
    public int GetCallCount { get; private set; }

    public override Task<OneOf<IReadOnlyList<CameraListItem>, Error>> GetCamerasAsync(
      string? status, CancellationToken ct)
    {
      GetCallCount++;
      if (ReturnError)
        return Task.FromResult(
          OneOf<IReadOnlyList<CameraListItem>, Error>.FromT1(
            new Error(Result.Unavailable, default, "api fail")));
      return Task.FromResult(
        OneOf<IReadOnlyList<CameraListItem>, Error>.FromT0((CameraList ?? []).ToList()));
    }
  }
}
