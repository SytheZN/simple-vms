using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
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
    var cameras = new List<CameraListItem>
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
    var cameras = new List<CameraListItem> { MakeCamera("Cam1", "192.168.1.1", "online") };
    var api = new GalleryApi { CameraList = cameras };
    var tunnel = new FakeStreamTunnel();
    var vm = new GalleryViewModel(api, tunnel, new FakeEventService(), NullLogger<GalleryViewModel>.Instance);

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(100);

    Assert.That(vm.Cameras, Has.Count.EqualTo(1));
  }

  private static CameraListItem MakeCamera(string name, string address, string status) => new()
  {
    Id = Guid.NewGuid(), Name = name, Address = address,
    Status = status, ProviderId = "onvif", Streams = [], Capabilities = []
  };

  private sealed class GalleryApi : FakeApiClient
  {
    public List<CameraListItem>? CameraList { get; set; }

    public override Task<OneOf<IReadOnlyList<CameraListItem>, Error>> GetCamerasAsync(
      string? status, CancellationToken ct) =>
      Task.FromResult(
        OneOf<IReadOnlyList<CameraListItem>, Error>.FromT0((CameraList ?? []).ToList()));
  }
}
