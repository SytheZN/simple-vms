using System.Threading.Channels;
using Client.Core.Decoding;
using Client.Core.Decoding.Diagnostics;
using Client.Core.Streaming;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class CameraViewModelTests
{
  private static readonly CameraListItem TestCamera = new()
  {
    Id = Guid.NewGuid(),
    Name = "Test Camera",
    Address = "192.168.1.100",
    Status = "online",
    ProviderId = "onvif",
    Streams = [
      new StreamProfileDto { Profile = "main", Codec = "h264", Resolution = "1920x1080", Fps = 30, RecordingEnabled = true },
      new StreamProfileDto { Profile = "sub", Codec = "h264", Resolution = "640x360", Fps = 15, RecordingEnabled = false }
    ],
    Capabilities = ["events"]
  };

  /// <summary>
  /// SCENARIO:
  /// LoadAsync fetches the camera metadata
  ///
  /// ACTION:
  /// Call LoadAsync
  ///
  /// EXPECTED RESULT:
  /// Camera is set and Player is created
  /// </summary>
  [Test]
  public async Task Load_FetchesCameraAndCreatesPlayer()
  {
    var (vm, _, _, api) = CreateVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);

    Assert.That(vm.Camera, Is.Not.Null);
    Assert.That(vm.Camera!.Id, Is.EqualTo(TestCamera.Id));
    Assert.That(vm.Player, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// GoLiveAsync subscribes to live stream
  ///
  /// ACTION:
  /// Load a camera, then call GoLiveAsync
  ///
  /// EXPECTED RESULT:
  /// LiveStreamService.SubscribeAsync is called once with the main profile
  /// </summary>
  [Test]
  public async Task GoLive_Subscribes()
  {
    var (vm, live, _, api) = CreateVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    await vm.GoLiveAsync(CancellationToken.None);

    Assert.That(live.SubscribeCount, Is.EqualTo(1));
    Assert.That(live.LastProfile, Is.EqualTo("main"));
    Assert.That(vm.IsPlayback, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// StartPlaybackAsync after being live
  ///
  /// ACTION:
  /// Go live, then start playback
  ///
  /// EXPECTED RESULT:
  /// Live feed is unsubscribed, playback start is called, IsPlayback becomes true
  /// </summary>
  [Test]
  public async Task StartPlayback_SwitchesFromLive()
  {
    var (vm, live, playback, api) = CreateVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    await vm.GoLiveAsync(CancellationToken.None);
    await vm.StartPlaybackAsync(1_000_000, 2_000_000, CancellationToken.None);

    Assert.That(playback.StartCount, Is.EqualTo(1));
    Assert.That(live.UnsubscribeCount, Is.EqualTo(1));
    Assert.That(vm.IsPlayback, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// SeekAsync during playback
  ///
  /// ACTION:
  /// Start playback, then seek to a different timestamp
  ///
  /// EXPECTED RESULT:
  /// Playback service is invoked to start a new stream at the new timestamp
  /// </summary>
  [Test]
  public async Task Seek_DuringPlayback_CallsPlaybackStart()
  {
    var (vm, _, playback, api) = CreateVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    await vm.StartPlaybackAsync(1_000_000, null, CancellationToken.None);
    await vm.SeekAsync(5_000_000, CancellationToken.None);

    Assert.That(playback.StartCount, Is.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// The CameraView rate slider signals a new playback rate via vm.SetRate
  ///
  /// ACTION:
  /// Load the camera, start playback, then call SetRate(2.0)
  ///
  /// EXPECTED RESULT:
  /// Player.Rate reports the new value (the view wiring path ends here)
  /// </summary>
  [Test]
  public async Task SetRate_UpdatesPlayerRate()
  {
    var (vm, _, _, api) = CreateVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    await vm.StartPlaybackAsync(1_000_000, null, CancellationToken.None);

    vm.SetRate(2.0);

    Assert.That(vm.Player!.Rate, Is.EqualTo(2.0));
    Assert.That(vm.Player.Direction, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// The CameraView play/pause button signals a toggle via vm.TogglePause
  ///
  /// ACTION:
  /// Load the camera, go live, call TogglePause twice
  ///
  /// EXPECTED RESULT:
  /// Player.Paused flips on the first call and back on the second
  /// </summary>
  [Test]
  public async Task TogglePause_FlipsPlayerPaused()
  {
    var (vm, _, _, api) = CreateVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    await vm.GoLiveAsync(CancellationToken.None);

    Assert.That(vm.Player!.Paused, Is.False);
    vm.TogglePause();
    Assert.That(vm.Player.Paused, Is.True);
    vm.TogglePause();
    Assert.That(vm.Player.Paused, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// The tunnel is already connected when the caller invokes WaitForTunnelConnectedAsync
  ///
  /// ACTION:
  /// Create a VM with a FakeStreamTunnel in the Connected state, call WaitForTunnelConnectedAsync
  ///
  /// EXPECTED RESULT:
  /// Returns true synchronously without waiting
  /// </summary>
  [Test]
  public async Task WaitForTunnelConnected_AlreadyConnected_ReturnsTrue()
  {
    var (vm, _, _, _) = CreateVm();

    var connected = await vm.WaitForTunnelConnectedAsync(
      TimeSpan.FromSeconds(1), CancellationToken.None);

    Assert.That(connected, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// The tunnel is disconnected and transitions to Connected while the caller waits
  ///
  /// ACTION:
  /// Set tunnel Disconnected, start WaitForTunnelConnectedAsync, then fire Connected
  ///
  /// EXPECTED RESULT:
  /// The wait completes with true
  /// </summary>
  [Test]
  public async Task WaitForTunnelConnected_FiresOnStateChange()
  {
    var api = new CameraApi();
    var tunnel = new FakeStreamTunnel { State = ConnectionState.Disconnected };
    var vm = new CameraViewModel(api, new FakeLive(), new FakePlayback(), tunnel,
      NullLogger<CameraViewModel>.Instance, NullLoggerFactory.Instance,
      new DecodePipelineFactory(NullLoggerFactory.Instance),
      new DiagnosticsSettings());

    var wait = vm.WaitForTunnelConnectedAsync(
      TimeSpan.FromSeconds(1), CancellationToken.None);
    Assert.That(wait.IsCompleted, Is.False);

    tunnel.FireStateChanged(ConnectionState.Connected);

    var connected = await wait;
    Assert.That(connected, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// The tunnel stays disconnected and the timeout expires
  ///
  /// ACTION:
  /// Set tunnel Disconnected, call WaitForTunnelConnectedAsync with a short timeout
  ///
  /// EXPECTED RESULT:
  /// Returns false without the tunnel ever firing Connected
  /// </summary>
  [Test]
  public async Task WaitForTunnelConnected_TimesOut()
  {
    var api = new CameraApi();
    var tunnel = new FakeStreamTunnel { State = ConnectionState.Disconnected };
    var vm = new CameraViewModel(api, new FakeLive(), new FakePlayback(), tunnel,
      NullLogger<CameraViewModel>.Instance, NullLoggerFactory.Instance,
      new DecodePipelineFactory(NullLoggerFactory.Instance),
      new DiagnosticsSettings());

    var connected = await vm.WaitForTunnelConnectedAsync(
      TimeSpan.FromMilliseconds(50), CancellationToken.None);

    Assert.That(connected, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// SelectedProfile changes while live
  ///
  /// ACTION:
  /// Go live, then switch profile
  ///
  /// EXPECTED RESULT:
  /// Old feed is unsubscribed and a new subscription opens with the new profile
  /// </summary>
  [Test]
  public async Task SwitchProfile_WhileLive_Resubscribes()
  {
    var (vm, live, _, api) = CreateVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    await vm.GoLiveAsync(CancellationToken.None);
    Assert.That(live.SubscribeCount, Is.EqualTo(1));

    vm.SelectedProfile = "sub";
    await Task.Delay(100);

    Assert.That(live.UnsubscribeCount, Is.GreaterThanOrEqualTo(1));
    Assert.That(live.SubscribeCount, Is.EqualTo(2));
    Assert.That(live.LastProfile, Is.EqualTo("sub"));
  }

  private static (CameraViewModel Vm, FakeLive Live, FakePlayback Playback, CameraApi Api) CreateVm()
  {
    var api = new CameraApi();
    var live = new FakeLive();
    var playback = new FakePlayback();
    var tunnel = new FakeStreamTunnel();
    var vm = new CameraViewModel(api, live, playback, tunnel,
      NullLogger<CameraViewModel>.Instance, NullLoggerFactory.Instance,
      new DecodePipelineFactory(NullLoggerFactory.Instance),
      new DiagnosticsSettings());
    return (vm, live, playback, api);
  }

  private sealed class CameraApi : FakeApiClient
  {
    public CameraListItem? Camera { get; set; }

    public override Task<OneOf<CameraListItem, Error>> GetCameraAsync(Guid id, CancellationToken ct) =>
      Camera != null
        ? Task.FromResult<OneOf<CameraListItem, Error>>(Camera)
        : Task.FromResult<OneOf<CameraListItem, Error>>(new Error(Result.Unavailable, default, "not found"));
  }

  private static VideoFeed MakeFeed(Guid cameraId, string profile)
  {
    var transport = new MemoryStream();
    var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);
    var channel = Channel.CreateUnbounded<MuxMessage>();
    var stream = new MuxStream(muxer, 1, channel.Reader, NullLogger.Instance);
    return new VideoFeed(stream, cameraId, profile, NullLogger.Instance);
  }

  private sealed class FakeLive : ILiveStreamService
  {
    public event Action<IVideoFeed, IVideoFeed>? FeedReplaced;
    public int SubscribeCount { get; private set; }
    public int UnsubscribeCount { get; private set; }
    public string? LastProfile { get; private set; }

    public void SimulateFeedReplaced(IVideoFeed oldFeed, IVideoFeed newFeed) =>
      FeedReplaced?.Invoke(oldFeed, newFeed);

    public Task<IVideoFeed> SubscribeAsync(Guid cameraId, string profile, CancellationToken ct)
    {
      SubscribeCount++;
      LastProfile = profile;
      return Task.FromResult<IVideoFeed>(MakeFeed(cameraId, profile));
    }

    public Task UnsubscribeAsync(IVideoFeed feed, CancellationToken ct)
    {
      UnsubscribeCount++;
      return feed.DisposeAsync().AsTask();
    }
  }

  private sealed class FakePlayback : IPlaybackService
  {
    public int StartCount { get; private set; }
    public int SeekCount { get; private set; }

    public Task<IVideoFeed> StartAsync(Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct)
    {
      StartCount++;
      return Task.FromResult<IVideoFeed>(MakeFeed(cameraId, profile));
    }

    public Task<IVideoFeed> SeekAsync(IVideoFeed current, ulong timestamp, CancellationToken ct)
    {
      SeekCount++;
      return StartAsync(current.CameraId, current.Profile, timestamp, null, ct);
    }

    public Task StopAsync(IVideoFeed feed, CancellationToken ct) =>
      feed.DisposeAsync().AsTask();
  }
}
