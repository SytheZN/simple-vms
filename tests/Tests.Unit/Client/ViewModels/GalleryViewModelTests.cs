using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Api;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class GalleryViewModelTests
{
  /// <summary>
  /// SCENARIO:
  /// LoadAsync is called and the API returns cameras
  ///
  /// ACTION:
  /// Call LoadAsync
  ///
  /// EXPECTED RESULT:
  /// Cameras collection is populated with the returned items
  /// </summary>
  [Test]
  public async Task Load_PopulatesCameras()
  {
    var cameras = new List<CameraDto>
    {
      MakeCamera("Cam1", "192.168.1.1", "online"),
      MakeCamera("Cam2", "192.168.1.2", "offline")
    };

    var api = new GalleryApi { CameraList = cameras };
    var tunnel = new FakeStreamTunnel();
    var vm = new GalleryViewModel(api, tunnel, new FakeEventService(), NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.That(vm.Cameras, Has.Count.EqualTo(2));
    Assert.That(vm.Cameras[0].Name, Is.EqualTo("Cam1"));
    Assert.That(vm.Cameras[1].Name, Is.EqualTo("Cam2"));
  }

  /// <summary>
  /// SCENARIO:
  /// Columns property is set
  ///
  /// ACTION:
  /// Set Columns to 4
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged fires for Columns
  /// </summary>
  [Test]
  public void Columns_Set_FiresPropertyChanged()
  {
    var vm = new GalleryViewModel(new GalleryApi(), new FakeStreamTunnel(), new FakeEventService(), NullLogger<GalleryViewModel>.Instance);

    var changed = new List<string>();
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

    vm.Columns = 4;

    Assert.That(changed, Does.Contain("Columns"));
    Assert.That(vm.Columns, Is.EqualTo(4));
  }

  /// <summary>
  /// SCENARIO:
  /// The tunnel reconnects
  ///
  /// ACTION:
  /// Fire StateChanged(Connected) on the tunnel
  ///
  /// EXPECTED RESULT:
  /// LoadAsync is called (cameras refreshed)
  /// </summary>
  [Test]
  public async Task Reconnect_RefreshesCameras()
  {
    var cameras = new List<CameraDto> { MakeCamera("Cam1", "192.168.1.1", "online") };
    var api = new GalleryApi { CameraList = cameras };
    var tunnel = new FakeStreamTunnel();
    var vm = new GalleryViewModel(api, tunnel, new FakeEventService(), NullLogger<GalleryViewModel>.Instance);

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(100);

    Assert.That(vm.Cameras, Has.Count.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A reload returns the same cameras with one status changed
  ///
  /// ACTION:
  /// Load once, then load again with a changed status on the second camera
  ///
  /// EXPECTED RESULT:
  /// The unchanged camera keeps its original instance (no container rebuild) and only
  /// the changed one is replaced
  /// </summary>
  [Test]
  public async Task Reload_ReplacesOnlyChangedCameras()
  {
    var first = MakeCamera("Cam1", "192.168.1.1", "online");
    var second = MakeCamera("Cam2", "192.168.1.2", "online");

    var api = new GalleryApi { CameraList = [first, second] };
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), new FakeEventService(),
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);
    var unchangedBefore = vm.Cameras[0];

    api.CameraList =
    [
      MakeCamera(first.Id, "Cam1", "192.168.1.1", "online"),
      MakeCamera(second.Id, "Cam2", "192.168.1.2", "offline")
    ];
    await vm.LoadAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.Cameras, Has.Count.EqualTo(2));
      Assert.That(vm.Cameras[0], Is.SameAs(unchangedBefore));
      Assert.That(vm.Cameras[1].Status, Is.EqualTo("offline"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A reload adds one camera and removes another
  ///
  /// ACTION:
  /// Load two cameras, then load a list dropping the first and appending a third
  ///
  /// EXPECTED RESULT:
  /// The surviving camera keeps its instance; the collection matches the new list order
  /// </summary>
  [Test]
  public async Task Reload_AddsAndRemovesInPlace()
  {
    var first = MakeCamera("Cam1", "192.168.1.1", "online");
    var second = MakeCamera("Cam2", "192.168.1.2", "online");

    var api = new GalleryApi { CameraList = [first, second] };
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), new FakeEventService(),
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);
    var survivor = vm.Cameras[1];

    api.CameraList = [second, MakeCamera("Cam3", "192.168.1.3", "online")];
    await vm.LoadAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.Cameras, Has.Count.EqualTo(2));
      Assert.That(vm.Cameras[0], Is.SameAs(survivor));
      Assert.That(vm.Cameras[1].Name, Is.EqualTo("Cam3"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A refresh runs while cameras are already displayed
  ///
  /// ACTION:
  /// Load once, then load again
  ///
  /// EXPECTED RESULT:
  /// The second load never sets IsLoading, so the refresh happens without showing the spinner
  /// </summary>
  [Test]
  public async Task Reload_WithExistingCameras_NeverShowsSpinner()
  {
    var api = new GalleryApi { CameraList = [MakeCamera("Cam1", "192.168.1.1", "online")] };
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), new FakeEventService(),
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    var sawLoading = false;
    vm.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(GalleryViewModel.IsLoading) && vm.IsLoading) sawLoading = true;
    };

    await vm.LoadAsync(CancellationToken.None);

    Assert.That(sawLoading, Is.False);
  }

  private static CameraDto MakeCamera(Guid id, string name, string address, string status) => new()
  {
    Id = id, Name = name, Address = address,
    Status = status, ProviderId = "onvif", Streams = [], Capabilities = []
  };

  private static CameraDto MakeCamera(string name, string address, string status) => new()
  {
    Id = Guid.NewGuid(), Name = name, Address = address,
    Status = status, ProviderId = "onvif", Streams = [], Capabilities = []
  };

  private sealed class GalleryApi : FakeApiClient
  {
    public List<CameraDto>? CameraList { get; set; }

    public override Task<OneOf<IReadOnlyList<CameraDto>, Error>> GetCamerasAsync(
      string? status, CancellationToken ct) =>
      Task.FromResult(
        OneOf<IReadOnlyList<CameraDto>, Error>.FromT0((CameraList ?? []).ToList()));
  }
}
