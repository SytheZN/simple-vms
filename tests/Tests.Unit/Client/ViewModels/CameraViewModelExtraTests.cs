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
public class CameraViewModelExtraTests
{
  private static readonly CameraListItem TestCamera = new()
  {
    Id = Guid.NewGuid(),
    Name = "Test Camera",
    Address = "192.168.1.100",
    Status = "online",
    ProviderId = "onvif",
    Streams = [],
    Capabilities = []
  };

  /// <summary>
  /// SCENARIO:
  /// LoadAsync hits an api error
  ///
  /// ACTION:
  /// Configure the api fake with no camera, call LoadAsync
  ///
  /// EXPECTED RESULT:
  /// ErrorMessage is populated; Camera stays null; Player is not created
  /// </summary>
  [Test]
  public async Task Load_ApiError_SetsErrorAndNoPlayer()
  {
    var (vm, _, _, _) = NewVm();

    await vm.LoadAsync(Guid.NewGuid(), CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.Camera, Is.Null);
      Assert.That(vm.ErrorMessage, Is.EqualTo("not found"));
      Assert.That(vm.Player, Is.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// LoadAsync is called twice for the same camera (e.g. user navigates back)
  ///
  /// ACTION:
  /// Load successful, Load again
  ///
  /// EXPECTED RESULT:
  /// Player is created exactly once (re-entry hits the player-not-null guard)
  /// </summary>
  [Test]
  public async Task Load_Twice_PlayerCreatedOnce()
  {
    var (vm, _, _, api) = NewVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    var firstPlayer = vm.Player;
    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);

    Assert.That(vm.Player, Is.SameAs(firstPlayer));
  }

  /// <summary>
  /// SCENARIO:
  /// MotionOverlay setter true subscribes to the motion profile feed
  ///
  /// ACTION:
  /// Load, set MotionOverlay = true
  ///
  /// EXPECTED RESULT:
  /// LiveStreamService.SubscribeAsync was called once with the "motion" profile;
  /// vm.MotionFeed is populated
  /// </summary>
  [Test]
  public async Task MotionOverlay_True_SubscribesToMotionFeed()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = TestCamera;
    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);

    vm.MotionOverlay = true;
    await Task.Delay(50);

    Assert.Multiple(() =>
    {
      Assert.That(live.SubscribeCount, Is.EqualTo(1));
      Assert.That(live.LastProfile, Is.EqualTo("motion"));
      Assert.That(vm.MotionFeed, Is.Not.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// MotionOverlay flips back to false after being on
  ///
  /// ACTION:
  /// Load, MotionOverlay = true (wait), MotionOverlay = false
  ///
  /// EXPECTED RESULT:
  /// LiveStreamService.UnsubscribeAsync was called; MotionFeed is null
  /// </summary>
  [Test]
  public async Task MotionOverlay_FalseAfterTrue_Unsubscribes()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = TestCamera;
    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);

    vm.MotionOverlay = true;
    await Task.Delay(50);

    vm.MotionOverlay = false;
    await Task.Delay(50);

    Assert.Multiple(() =>
    {
      Assert.That(live.UnsubscribeCount, Is.GreaterThanOrEqualTo(1));
      Assert.That(vm.MotionFeed, Is.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// MotionOverlay is set true with no camera loaded
  ///
  /// ACTION:
  /// Set MotionOverlay = true without LoadAsync
  ///
  /// EXPECTED RESULT:
  /// No subscription; no exception (the early-out guard is honoured)
  /// </summary>
  [Test]
  public async Task MotionOverlay_NoCamera_NoSubscribe()
  {
    var (vm, live, _, _) = NewVm();

    vm.MotionOverlay = true;
    await Task.Delay(50);

    Assert.That(live.SubscribeCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// SwitchProfile fires before LoadAsync (no player exists)
  ///
  /// ACTION:
  /// Set SelectedProfile on a fresh VM
  ///
  /// EXPECTED RESULT:
  /// No live subscription opens (player-null guard)
  /// </summary>
  [Test]
  public async Task SwitchProfile_NoPlayer_NoOp()
  {
    var (vm, live, _, _) = NewVm();

    vm.SelectedProfile = "sub";
    await Task.Delay(50);

    Assert.That(live.SubscribeCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// IsTunnelConnected reflects the tunnel state at the moment of access
  ///
  /// ACTION:
  /// Construct VM with a tunnel that's Connected, then flip it Disconnected
  ///
  /// EXPECTED RESULT:
  /// The getter tracks the current state without caching
  /// </summary>
  [Test]
  public void IsTunnelConnected_TracksTunnelStateLive()
  {
    var api = new CameraApi();
    var tunnel = new FakeStreamTunnel { State = ConnectionState.Connected };
    var vm = new CameraViewModel(api, new FakeLive(), new FakePlayback(), tunnel,
      NullLogger<CameraViewModel>.Instance, NullLoggerFactory.Instance,
      new DecodePipelineFactory(NullLoggerFactory.Instance),
      new DiagnosticsSettings());

    Assert.That(vm.IsTunnelConnected, Is.True);

    tunnel.State = ConnectionState.Disconnected;

    Assert.That(vm.IsTunnelConnected, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// ScrubStart pauses the underlying player so frames stop advancing
  ///
  /// ACTION:
  /// Load, GoLive, ScrubStart
  ///
  /// EXPECTED RESULT:
  /// Player.Paused is true
  /// </summary>
  [Test]
  public async Task ScrubStart_PausesPlayer()
  {
    var (vm, _, _, api) = NewVm();
    api.Camera = TestCamera;
    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    await vm.GoLiveAsync(CancellationToken.None);

    vm.ScrubStart();

    Assert.That(vm.Player!.Paused, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// ScrubEndAsync ends scrub by seeking to the released position
  ///
  /// ACTION:
  /// Load, GoLive, ScrubStart, ScrubEndAsync(1500000)
  ///
  /// EXPECTED RESULT:
  /// Playback.StartCount increments (ScrubEnd seeks via the playback service)
  /// </summary>
  [Test]
  public async Task ScrubEndAsync_SeeksToReleasedPosition()
  {
    var (vm, _, playback, api) = NewVm();
    api.Camera = TestCamera;
    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    await vm.GoLiveAsync(CancellationToken.None);

    vm.ScrubStart();
    await vm.ScrubEndAsync(1_500_000, CancellationToken.None);

    Assert.That(playback.StartCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// DisposeAsync after MotionOverlay was enabled
  ///
  /// ACTION:
  /// Load, MotionOverlay = true, DisposeAsync
  ///
  /// EXPECTED RESULT:
  /// Motion feed is unsubscribed and Player is torn down without exception
  /// </summary>
  [Test]
  public async Task Dispose_WithMotionFeed_UnsubscribesAndDisposesPlayer()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = TestCamera;
    await vm.LoadAsync(TestCamera.Id, CancellationToken.None);
    vm.MotionOverlay = true;
    await Task.Delay(50);

    await vm.DisposeAsync();

    Assert.That(live.UnsubscribeCount, Is.GreaterThanOrEqualTo(1));
    Assert.That(vm.MotionFeed, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// DisposeAsync is called on a never-loaded VM
  ///
  /// ACTION:
  /// Construct, DisposeAsync
  ///
  /// EXPECTED RESULT:
  /// Completes without exception (both player and motion-feed paths are skipped)
  /// </summary>
  [Test]
  public async Task Dispose_NeverLoaded_NoOp()
  {
    var (vm, _, _, _) = NewVm();

    await vm.DisposeAsync();
  }

  private static (CameraViewModel Vm, FakeLive Live, FakePlayback Playback, CameraApi Api) NewVm()
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

  private static VideoFeed MakeFeed(Guid cameraId, string profile)
  {
    var transport = new MemoryStream();
    var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);
    var channel = Channel.CreateUnbounded<MuxMessage>();
    var stream = new MuxStream(muxer, 1, channel.Reader, NullLogger.Instance);
    return new VideoFeed(stream, cameraId, profile, NullLogger.Instance);
  }

  private sealed class CameraApi : FakeApiClient
  {
    public CameraListItem? Camera { get; set; }

    public override Task<OneOf<CameraListItem, Error>> GetCameraAsync(Guid id, CancellationToken ct) =>
      Camera != null
        ? Task.FromResult<OneOf<CameraListItem, Error>>(Camera)
        : Task.FromResult<OneOf<CameraListItem, Error>>(new Error(Result.Unavailable, default, "not found"));
  }

  private sealed class FakeLive : ILiveStreamService
  {
    public event Action<IVideoFeed, IVideoFeed>? FeedReplaced;
    public int SubscribeCount { get; private set; }
    public int UnsubscribeCount { get; private set; }
    public string? LastProfile { get; private set; }

    public void RaiseFeedReplaced(IVideoFeed o, IVideoFeed n) => FeedReplaced?.Invoke(o, n);

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

    public Task<IVideoFeed> StartAsync(Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct)
    {
      StartCount++;
      return Task.FromResult<IVideoFeed>(MakeFeed(cameraId, profile));
    }

    public Task<IVideoFeed> SeekAsync(IVideoFeed current, ulong timestamp, CancellationToken ct) =>
      StartAsync(current.CameraId, current.Profile, timestamp, null, ct);

    public Task StopAsync(IVideoFeed feed, CancellationToken ct) => feed.DisposeAsync().AsTask();
  }
}
