using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Api;
using Shared.Protocol;
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
    var vm = new GalleryViewModel(api, tunnel, new FakeEventService(), new FakeGalleryThumbnails(), NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.That(vm.Cameras, Has.Count.EqualTo(2));
    Assert.That(vm.Cameras[0].Camera.Name, Is.EqualTo("Cam1"));
    Assert.That(vm.Cameras[1].Camera.Name, Is.EqualTo("Cam2"));
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
    var vm = new GalleryViewModel(new GalleryApi(), new FakeStreamTunnel(), new FakeEventService(), new FakeGalleryThumbnails(), NullLogger<GalleryViewModel>.Instance);

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
    var vm = new GalleryViewModel(api, tunnel, new FakeEventService(), new FakeGalleryThumbnails(), NullLogger<GalleryViewModel>.Instance);

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
  /// Both tiles survive the reload, so neither loses its thumbnail, and only the changed camera's
  /// record is swapped
  /// </summary>
  [Test]
  public async Task Reload_KeepsTilesAndSwapsOnlyChangedCameras()
  {
    var first = MakeCamera("Cam1", "192.168.1.1", "online");
    var second = MakeCamera("Cam2", "192.168.1.2", "online");

    var api = new GalleryApi { CameraList = [first, second] };
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), new FakeEventService(), new FakeGalleryThumbnails(),
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);
    var unchangedTile = vm.Cameras[0];
    var changedTile = vm.Cameras[1];
    var unchangedCameraBefore = unchangedTile.Camera;

    api.CameraList =
    [
      MakeCamera(first.Id, "Cam1", "192.168.1.1", "online"),
      MakeCamera(second.Id, "Cam2", "192.168.1.2", "offline")
    ];
    await vm.LoadAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.Cameras, Has.Count.EqualTo(2));
      Assert.That(vm.Cameras[0], Is.SameAs(unchangedTile));
      Assert.That(vm.Cameras[1], Is.SameAs(changedTile));
      Assert.That(vm.Cameras[0].Camera, Is.SameAs(unchangedCameraBefore));
      Assert.That(vm.Cameras[1].Camera.Status, Is.EqualTo("offline"));
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
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), new FakeEventService(), new FakeGalleryThumbnails(),
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);
    var survivor = vm.Cameras[1];

    api.CameraList = [second, MakeCamera("Cam3", "192.168.1.3", "online")];
    await vm.LoadAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.Cameras, Has.Count.EqualTo(2));
      Assert.That(vm.Cameras[0], Is.SameAs(survivor));
      Assert.That(vm.Cameras[1].Camera.Name, Is.EqualTo("Cam3"));
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
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), new FakeEventService(), new FakeGalleryThumbnails(),
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

  /// <summary>
  /// SCENARIO:
  /// The gallery loads its cameras
  ///
  /// ACTION:
  /// Call LoadAsync
  ///
  /// EXPECTED RESULT:
  /// The tiles are handed to the thumbnail subscriptions, which is what starts them arriving
  /// </summary>
  [Test]
  public async Task Load_SyncsThumbnails()
  {
    var api = new GalleryApi { CameraList = [MakeCamera("Cam1", "192.168.1.1", "online")] };
    var thumbnails = new FakeGalleryThumbnails();
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), new FakeEventService(), thumbnails,
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    Assert.That(thumbnails.Synced, Has.Count.EqualTo(1));
    Assert.That(thumbnails.Synced[0], Has.Count.EqualTo(1));
    Assert.That(thumbnails.Synced[0][0].Camera.Name, Is.EqualTo("Cam1"));
  }

  /// <summary>
  /// SCENARIO:
  /// A shell that keeps the gallery alive navigates away from it
  ///
  /// ACTION:
  /// Load, suspend, then load again
  ///
  /// EXPECTED RESULT:
  /// The streams are released while the gallery is off screen and picked up again on return
  /// </summary>
  [Test]
  public async Task Suspend_ReleasesThumbnailsUntilNextLoad()
  {
    var api = new GalleryApi { CameraList = [MakeCamera("Cam1", "192.168.1.1", "online")] };
    var thumbnails = new FakeGalleryThumbnails();
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), new FakeEventService(), thumbnails,
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);
    vm.Suspend();
    await vm.LoadAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(thumbnails.StopCount, Is.EqualTo(1));
      Assert.That(thumbnails.Synced, Has.Count.EqualTo(2));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The gallery is closed
  ///
  /// ACTION:
  /// Dispose the view model
  ///
  /// EXPECTED RESULT:
  /// The thumbnail subscriptions are released, so the analyzer stops producing for a gallery
  /// nobody is looking at
  /// </summary>
  [Test]
  public void Dispose_StopsThumbnails()
  {
    var thumbnails = new FakeGalleryThumbnails();
    var vm = new GalleryViewModel(new GalleryApi(), new FakeStreamTunnel(), new FakeEventService(),
      thumbnails, NullLogger<GalleryViewModel>.Instance);

    vm.Dispose();

    Assert.That(thumbnails.StopCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is reconfigured while the gallery is showing it
  ///
  /// ACTION:
  /// Fire a "config" event on the event channel
  ///
  /// EXPECTED RESULT:
  /// The list is refreshed and the tile is not highlighted, because nothing happened in front of
  /// the camera
  /// </summary>
  [Test]
  public async Task Event_Config_RefreshesWithoutHighlighting()
  {
    var camera = MakeCamera("Cam1", "192.168.1.1", "online");
    var api = new GalleryApi { CameraList = [camera] };
    var events = new FakeEventService();
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), events, new FakeGalleryThumbnails(),
      NullLogger<GalleryViewModel>.Instance);

    var highlighted = new List<Guid>();
    vm.CameraEventReceived += id => highlighted.Add(id);

    events.Fire(
      new EventChannelMessage { CameraId = camera.Id, Type = "__config", StartTime = 1000 },
      EventChannelFlags.Start);
    await Task.Delay(100);

    Assert.Multiple(() =>
    {
      Assert.That(vm.Cameras, Has.Count.EqualTo(1));
      Assert.That(highlighted, Is.Empty);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A pipeline goes offline while the gallery is showing the camera
  ///
  /// ACTION:
  /// Fire a "status" event on the event channel
  ///
  /// EXPECTED RESULT:
  /// The list is refreshed so the badge stops claiming the old state, without highlighting
  /// </summary>
  [Test]
  public async Task Event_Status_RefreshesWithoutHighlighting()
  {
    var camera = MakeCamera("Cam1", "192.168.1.1", "online");
    var api = new GalleryApi { CameraList = [camera] };
    var events = new FakeEventService();
    var vm = new GalleryViewModel(api, new FakeStreamTunnel(), events, new FakeGalleryThumbnails(),
      NullLogger<GalleryViewModel>.Instance);

    await vm.LoadAsync(CancellationToken.None);

    var highlighted = new List<Guid>();
    vm.CameraEventReceived += id => highlighted.Add(id);

    api.CameraList = [MakeCamera(camera.Id, "Cam1", "192.168.1.1", "offline")];
    events.Fire(
      new EventChannelMessage { CameraId = camera.Id, Type = "__status", StartTime = 1000 },
      EventChannelFlags.Start);
    await Task.Delay(100);

    Assert.Multiple(() =>
    {
      Assert.That(vm.Cameras[0].Camera.Status, Is.EqualTo("offline"));
      Assert.That(highlighted, Is.Empty);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Motion is detected on a camera
  ///
  /// ACTION:
  /// Fire a "motion" event on the event channel
  ///
  /// EXPECTED RESULT:
  /// The camera is highlighted
  /// </summary>
  [Test]
  public void Event_Motion_Highlights()
  {
    var cameraId = Guid.NewGuid();
    var events = new FakeEventService();
    var vm = new GalleryViewModel(new GalleryApi(), new FakeStreamTunnel(), events, new FakeGalleryThumbnails(),
      NullLogger<GalleryViewModel>.Instance);

    var highlighted = new List<Guid>();
    vm.CameraEventReceived += id => highlighted.Add(id);

    events.Fire(
      new EventChannelMessage { CameraId = cameraId, Type = "motion", StartTime = 1000 },
      EventChannelFlags.Start);

    Assert.That(highlighted, Is.EqualTo(new[] { cameraId }));
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
