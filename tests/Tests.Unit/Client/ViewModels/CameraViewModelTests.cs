using System.Threading.Channels;
using Client.Core.Streaming;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
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
  /// GoLiveAsync is called on the CameraViewModel
  ///
  /// ACTION:
  /// Load a camera, then go live
  ///
  /// EXPECTED RESULT:
  /// LiveStreamService.SubscribeAsync is called, VideoFeed is set, IsPlayback is false
  /// </summary>
  [Test]
  public async Task GoLive_SubscribesAndSetsFeed()
  {
    var (vm, live, _) = CreateVm();

    vm.Camera = TestCamera;
    await vm.GoLiveAsync(CancellationToken.None);

    Assert.That(vm.VideoFeed, Is.Not.Null);
    Assert.That(vm.IsPlayback, Is.False);
    Assert.That(live.SubscribeCount, Is.EqualTo(1));
    Assert.That(live.LastProfile, Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// StartPlaybackAsync is called after being live
  ///
  /// ACTION:
  /// Go live, then start playback
  ///
  /// EXPECTED RESULT:
  /// Live feed is unsubscribed, playback feed is started, IsPlayback is true
  /// </summary>
  [Test]
  public async Task StartPlayback_SwitchesFromLive()
  {
    var (vm, live, playback) = CreateVm();

    vm.Camera = TestCamera;
    await vm.GoLiveAsync(CancellationToken.None);
    Assert.That(vm.IsPlayback, Is.False);

    await vm.StartPlaybackAsync(1_000_000, 2_000_000, CancellationToken.None);
    Assert.That(vm.IsPlayback, Is.True);
    Assert.That(vm.VideoFeed, Is.Not.Null);
    Assert.That(playback.StartCount, Is.EqualTo(1));
    Assert.That(live.UnsubscribeCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// SeekAsync is called during playback
  ///
  /// ACTION:
  /// Start playback, then seek
  ///
  /// EXPECTED RESULT:
  /// PlaybackService.SeekAsync is called, VideoFeed is updated
  /// </summary>
  [Test]
  public async Task Seek_DuringPlayback_CallsPlaybackSeek()
  {
    var (vm, _, playback) = CreateVm();

    vm.Camera = TestCamera;
    await vm.StartPlaybackAsync(1_000_000, null, CancellationToken.None);
    var firstFeed = vm.VideoFeed;

    await vm.SeekAsync(5_000_000, CancellationToken.None);

    Assert.That(playback.SeekCount, Is.EqualTo(1));
    Assert.That(vm.VideoFeed, Is.Not.SameAs(firstFeed));
  }

  /// <summary>
  /// SCENARIO:
  /// SelectedProfile is changed while live
  ///
  /// ACTION:
  /// Go live with "main", then switch to "sub"
  ///
  /// EXPECTED RESULT:
  /// The old feed is unsubscribed and a new subscription is opened with "sub"
  /// </summary>
  [Test]
  public async Task SwitchProfile_WhileLive_Resubscribes()
  {
    var (vm, live, _) = CreateVm();

    vm.Camera = TestCamera;
    await vm.GoLiveAsync(CancellationToken.None);
    Assert.That(live.SubscribeCount, Is.EqualTo(1));

    vm.SelectedProfile = "sub";
    await Task.Delay(50);

    Assert.That(live.UnsubscribeCount, Is.EqualTo(1));
    Assert.That(live.SubscribeCount, Is.EqualTo(2));
    Assert.That(live.LastProfile, Is.EqualTo("sub"));
  }

  private static (CameraViewModel Vm, FakeLive Live, FakePlayback Playback) CreateVm()
  {
    var api = new FakeApiClient();
    var live = new FakeLive();
    var playback = new FakePlayback();
    return (new CameraViewModel(api, live, playback), live, playback);
  }

  private static VideoFeed MakeFeed(Guid cameraId, string profile)
  {
    var transport = new MemoryStream();
    var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);
    var channel = Channel.CreateUnbounded<MuxMessage>();
    var stream = new MuxStream(muxer, 1, channel.Reader);
    return new VideoFeed(stream, cameraId, profile);
  }

  private sealed class FakeLive : ILiveStreamService
  {
    public event Action<VideoFeed, VideoFeed>? FeedReplaced;
    public int SubscribeCount { get; private set; }
    public int UnsubscribeCount { get; private set; }
    public string? LastProfile { get; private set; }

    public void SimulateFeedReplaced(VideoFeed oldFeed, VideoFeed newFeed) =>
      FeedReplaced?.Invoke(oldFeed, newFeed);

    public Task<VideoFeed> SubscribeAsync(Guid cameraId, string profile, CancellationToken ct)
    {
      SubscribeCount++;
      LastProfile = profile;
      return Task.FromResult(MakeFeed(cameraId, profile));
    }

    public Task UnsubscribeAsync(VideoFeed feed, CancellationToken ct)
    {
      UnsubscribeCount++;
      return feed.DisposeAsync().AsTask();
    }
  }

  private sealed class FakePlayback : IPlaybackService
  {
    public int StartCount { get; private set; }
    public int SeekCount { get; private set; }

    public Task<VideoFeed> StartAsync(Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct)
    {
      StartCount++;
      return Task.FromResult(MakeFeed(cameraId, profile));
    }

    public Task<VideoFeed> SeekAsync(VideoFeed current, ulong timestamp, CancellationToken ct)
    {
      SeekCount++;
      return StartAsync(current.CameraId, current.Profile, timestamp, null, ct);
    }

    public Task StopAsync(VideoFeed feed, CancellationToken ct) =>
      feed.DisposeAsync().AsTask();
  }
}
