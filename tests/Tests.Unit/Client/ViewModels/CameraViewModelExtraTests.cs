using System.Threading.Channels;
using Client.Core.Decoding;
using Client.Core.Decoding.Diagnostics;
using Client.Core.Streaming;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Api;
using Shared.Models;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class CameraViewModelExtraTests
{
  private static readonly CameraDto TestCamera = new()
  {
    Id = Guid.NewGuid(),
    Name = "Test Camera",
    Address = "192.168.1.100",
    Status = "online",
    ProviderId = "onvif",
    Streams = [
      new StreamProfileDto { Profile = "main", Kind = StreamKind.Quality, FormatId = "fmp4", Codec = "h264", Resolution = "1920x1080", Fps = 30, RecordingEnabled = true },
      new StreamProfileDto { Profile = "sub", Kind = StreamKind.Quality, FormatId = "fmp4", Codec = "h264", Resolution = "640x360", Fps = 15, RecordingEnabled = true }
    ],
    Capabilities = []
  };

  private static readonly CameraDto CameraWithOverlay = new()
  {
    Id = Guid.NewGuid(),
    Name = "Test Camera",
    Address = "192.168.1.100",
    Status = "online",
    ProviderId = "onvif",
    Streams = [
      new StreamProfileDto { Profile = "main", Kind = StreamKind.Quality, FormatId = "fmp4", Codec = "h264", Resolution = "1920x1080", Fps = 30, RecordingEnabled = true },
      new StreamProfileDto { Profile = "main-motion-grid", Kind = StreamKind.Metadata, FormatId = "motion-grid", Codec = "mgrd", Resolution = "240x135", Fps = 30, RecordingEnabled = false }
    ],
    Capabilities = []
  };

  private static readonly CameraDto CameraWithTwoOverlays = new()
  {
    Id = Guid.NewGuid(),
    Name = "Test Camera",
    Address = "192.168.1.100",
    Status = "online",
    ProviderId = "onvif",
    Streams = [
      new StreamProfileDto { Profile = "main", Kind = StreamKind.Quality, FormatId = "fmp4", Codec = "h264", Resolution = "1920x1080", Fps = 30, RecordingEnabled = true },
      new StreamProfileDto { Profile = "sub-motion-grid", Kind = StreamKind.Metadata, FormatId = "motion-grid", Codec = "mgrd", Resolution = "80x45", Fps = 15, RecordingEnabled = false },
      new StreamProfileDto { Profile = "main-motion-grid", Kind = StreamKind.Metadata, FormatId = "motion-grid", Codec = "mgrd", Resolution = "240x135", Fps = 30, RecordingEnabled = false }
    ],
    Capabilities = []
  };

  /// <summary>
  /// SCENARIO:
  /// LoadAsync is called with a camera that has no metadata streams
  ///
  /// ACTION:
  /// Load camera with only quality streams
  ///
  /// EXPECTED RESULT:
  /// OverlaySources is empty
  /// </summary>
  [Test]
  public async Task Load_NoMetadataStreams_OverlaySourcesEmpty()
  {
    var (vm, _, _, api) = NewVm();
    api.Camera = TestCamera;

    await vm.LoadAsync(TestCamera.Id, Quality.Highest, CancellationToken.None);

    Assert.That(vm.OverlaySources, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// LoadAsync is called with a camera that has one metadata stream
  ///
  /// ACTION:
  /// Load camera with a metadata stream
  ///
  /// EXPECTED RESULT:
  /// OverlaySources contains only the metadata stream; ActiveOverlaySource is null
  /// </summary>
  [Test]
  public async Task Load_MetadataStream_PopulatesOverlaySources()
  {
    var (vm, _, _, api) = NewVm();
    api.Camera = CameraWithOverlay;

    await vm.LoadAsync(CameraWithOverlay.Id, Quality.Highest, CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.OverlaySources, Has.Count.EqualTo(1));
      Assert.That(vm.OverlaySources[0].Profile, Is.EqualTo("main-motion-grid"));
      Assert.That(vm.ActiveOverlaySource, Is.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// ToggleOverlayAsync is called once on a camera with one metadata stream
  ///
  /// ACTION:
  /// Load camera, call ToggleOverlayAsync
  ///
  /// EXPECTED RESULT:
  /// ActiveOverlaySource is the metadata stream; Overlay is set; subscribe count is 1
  /// </summary>
  [Test]
  public async Task ToggleOverlay_On_ActivatesSource()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = CameraWithOverlay;
    await vm.LoadAsync(CameraWithOverlay.Id, Quality.Highest, CancellationToken.None);

    await vm.ToggleOverlayAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.ActiveOverlaySource, Is.Not.Null);
      Assert.That(vm.ActiveOverlaySource!.Profile, Is.EqualTo("main-motion-grid"));
      Assert.That(vm.Overlay, Is.Not.Null);
      Assert.That(live.SubscribeCount, Is.EqualTo(1));
      Assert.That(live.LastProfile, Is.EqualTo("main-motion-grid"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// ToggleOverlayAsync is called again while the overlay is active
  ///
  /// ACTION:
  /// Load camera, toggle twice
  ///
  /// EXPECTED RESULT:
  /// Second toggle deactivates; overlay feed is unsubscribed; Overlay is null
  /// </summary>
  [Test]
  public async Task ToggleOverlay_Off_Deactivates()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = CameraWithOverlay;
    await vm.LoadAsync(CameraWithOverlay.Id, Quality.Highest, CancellationToken.None);

    await vm.ToggleOverlayAsync(CancellationToken.None);
    await vm.ToggleOverlayAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(vm.ActiveOverlaySource, Is.Null);
      Assert.That(vm.Overlay, Is.Null);
      Assert.That(live.UnsubscribeCount, Is.EqualTo(1));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Two metadata streams exist and the selected video profile is "sub"
  ///
  /// ACTION:
  /// Load camera, select the "sub" video profile, toggle the overlay on
  ///
  /// EXPECTED RESULT:
  /// The source matching the video profile ("sub-motion-grid") wins even though
  /// "main-motion-grid" sorts first alphabetically
  /// </summary>
  [Test]
  public async Task ToggleOverlay_TwoSources_PrefersVideoProfileMatch()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = CameraWithTwoOverlays;
    await vm.LoadAsync(CameraWithTwoOverlays.Id, Quality.Highest, CancellationToken.None);
    vm.SelectedProfile = "sub";

    await vm.ToggleOverlayAsync(CancellationToken.None);

    Assert.That(vm.ActiveOverlaySource!.Profile, Is.EqualTo("sub-motion-grid"));
    Assert.That(live.LastProfile, Is.EqualTo("sub-motion-grid"));
  }

  /// <summary>
  /// SCENARIO:
  /// ToggleOverlayAsync is called with no camera loaded
  ///
  /// ACTION:
  /// Call ToggleOverlayAsync without loading a camera
  ///
  /// EXPECTED RESULT:
  /// No subscription opened; no exception
  /// </summary>
  [Test]
  public async Task ToggleOverlay_NoCamera_NoOp()
  {
    var (vm, live, _, _) = NewVm();

    await vm.ToggleOverlayAsync(CancellationToken.None);

    Assert.That(live.SubscribeCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// SeekAsync is called while the overlay is active on a live feed
  ///
  /// ACTION:
  /// Load camera, activate overlay, mark the overlay feed live, seek, tick the overlay
  /// with a playback view at the seek position
  ///
  /// EXPECTED RESULT:
  /// The seek reset clears the live gate, so the tick fetches from the playhead
  /// with a 30-second window
  /// </summary>
  [Test]
  public async Task SeekAsync_WithOverlayActive_ResetsOverlayForFetching()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = CameraWithOverlay;
    await vm.LoadAsync(CameraWithOverlay.Id, Quality.Highest, CancellationToken.None);
    await vm.ToggleOverlayAsync(CancellationToken.None);
    var feed = live.LastFeed!;
    feed.RaiseStatus(StreamStatus.Live);

    await vm.SeekAsync(10_000_000UL, CancellationToken.None);
    vm.Overlay!.Tick(new OverlayPlayerView(
      10_000_000, 1, 1, false, Player.PlayerMode.Playback));

    Assert.That(feed.FetchFrom, Is.EqualTo(10_000_000UL));
    Assert.That(feed.FetchTo, Is.EqualTo(10_000_000UL + 30_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// ScrubEndAsync is called while the overlay is active on a live feed
  ///
  /// ACTION:
  /// Load camera, activate overlay, mark the overlay feed live, scrub-end, tick the
  /// overlay with a playback view at the released position
  ///
  /// EXPECTED RESULT:
  /// The scrub-end reset clears the live gate, so the tick fetches from the released
  /// position with a 30-second window
  /// </summary>
  [Test]
  public async Task ScrubEndAsync_WithOverlayActive_ResetsOverlayForFetching()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = CameraWithOverlay;
    await vm.LoadAsync(CameraWithOverlay.Id, Quality.Highest, CancellationToken.None);
    await vm.ToggleOverlayAsync(CancellationToken.None);
    var feed = live.LastFeed!;
    feed.RaiseStatus(StreamStatus.Live);

    await vm.ScrubEndAsync(5_000_000, CancellationToken.None);
    vm.Overlay!.Tick(new OverlayPlayerView(
      5_000_000, 1, 1, false, Player.PlayerMode.Playback));

    Assert.That(feed.FetchFrom, Is.EqualTo(5_000_000UL));
    Assert.That(feed.FetchTo, Is.EqualTo(5_000_000UL + 30_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// GoLiveAsync is called while the overlay is active
  ///
  /// ACTION:
  /// Load camera, activate overlay, call GoLiveAsync
  ///
  /// EXPECTED RESULT:
  /// Overlay feed is unsubscribed and a new live subscription opens for the same profile
  /// </summary>
  [Test]
  public async Task GoLiveAsync_WithOverlayActive_ResubscribesOverlay()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = CameraWithOverlay;
    await vm.LoadAsync(CameraWithOverlay.Id, Quality.Highest, CancellationToken.None);
    await vm.ToggleOverlayAsync(CancellationToken.None);
    var subscribeCountAfterActivate = live.SubscribeCount;

    await vm.GoLiveAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(live.UnsubscribeCount, Is.GreaterThanOrEqualTo(1));
      Assert.That(live.SubscribeCount, Is.GreaterThan(subscribeCountAfterActivate));
      Assert.That(live.LastProfile, Is.EqualTo("main-motion-grid"));
      Assert.That(vm.Overlay, Is.Not.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// DisposeAsync is called while the overlay is active
  ///
  /// ACTION:
  /// Load camera, activate overlay, DisposeAsync
  ///
  /// EXPECTED RESULT:
  /// Motion feed is unsubscribed; Overlay is null
  /// </summary>
  [Test]
  public async Task Dispose_WithOverlayActive_UnsubscribesOverlay()
  {
    var (vm, live, _, api) = NewVm();
    api.Camera = CameraWithOverlay;
    await vm.LoadAsync(CameraWithOverlay.Id, Quality.Highest, CancellationToken.None);
    await vm.ToggleOverlayAsync(CancellationToken.None);

    await vm.DisposeAsync();

    Assert.Multiple(() =>
    {
      Assert.That(live.UnsubscribeCount, Is.GreaterThanOrEqualTo(1));
      Assert.That(vm.Overlay, Is.Null);
    });
  }

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

    await vm.LoadAsync(Guid.NewGuid(), Quality.Highest, CancellationToken.None);

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

    await vm.LoadAsync(TestCamera.Id, Quality.Highest, CancellationToken.None);
    var firstPlayer = vm.Player;
    await vm.LoadAsync(TestCamera.Id, Quality.Highest, CancellationToken.None);

    Assert.That(vm.Player, Is.SameAs(firstPlayer));
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
    await vm.LoadAsync(TestCamera.Id, Quality.Highest, CancellationToken.None);
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
    await vm.LoadAsync(TestCamera.Id, Quality.Highest, CancellationToken.None);
    await vm.GoLiveAsync(CancellationToken.None);

    vm.ScrubStart();
    await vm.ScrubEndAsync(1_500_000, CancellationToken.None);

    Assert.That(playback.StartCount, Is.EqualTo(1));
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
    public CameraDto? Camera { get; set; }

    public override Task<OneOf<CameraDto, Error>> GetCameraAsync(Guid id, CancellationToken ct) =>
      Camera != null
        ? Task.FromResult<OneOf<CameraDto, Error>>(Camera)
        : Task.FromResult<OneOf<CameraDto, Error>>(new Error(Result.Unavailable, default, "not found"));
  }

  private sealed class FakeVideoFeed : IVideoFeed
  {
    public Guid CameraId { get; }
    public string Profile { get; }
    public ReadOnlyMemory<byte> LastInit => ReadOnlyMemory<byte>.Empty;
    public ulong FetchFrom { get; private set; }
    public ulong FetchTo { get; private set; }

    public event Action<ReadOnlyMemory<byte>>? OnInit;
    public event Action<GopMessage>? OnGop;
    public event Action<StreamStatus>? OnStatus;
    public event Action<GapStatus>? OnGap;
    public event Action? OnCompleted;

    public FakeVideoFeed(Guid cameraId, string profile)
    {
      CameraId = cameraId;
      Profile = profile;
    }

    public void RaiseInit(ReadOnlyMemory<byte> data) => OnInit?.Invoke(data);
    public void RaiseGop(GopMessage gop) => OnGop?.Invoke(gop);
    public void RaiseStatus(StreamStatus s) => OnStatus?.Invoke(s);
    public void RaiseGap(GapStatus g) => OnGap?.Invoke(g);
    public void RaiseCompleted() => OnCompleted?.Invoke();

    public void Start() { }

    public Task SendFetchAsync(ulong from, ulong to, CancellationToken ct)
    {
      FetchFrom = from;
      FetchTo = to;
      return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FakeLive : ILiveStreamService
  {
    public event Action<IVideoFeed, IVideoFeed>? FeedReplaced;
    public int SubscribeCount { get; private set; }
    public int UnsubscribeCount { get; private set; }
    public string? LastProfile { get; private set; }
    public FakeVideoFeed? LastFeed { get; private set; }

    public void RaiseFeedReplaced(IVideoFeed o, IVideoFeed n) => FeedReplaced?.Invoke(o, n);

    public Task<IVideoFeed> SubscribeAsync(Guid cameraId, string profile, CancellationToken ct)
    {
      SubscribeCount++;
      LastProfile = profile;
      LastFeed = new FakeVideoFeed(cameraId, profile);
      return Task.FromResult<IVideoFeed>(LastFeed);
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
