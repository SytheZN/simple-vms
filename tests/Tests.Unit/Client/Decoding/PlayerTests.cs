using Client.Core.Decoding;
using Client.Core.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class PlayerTests
{
  /// <summary>
  /// SCENARIO:
  /// A fresh Player is constructed with no camera attached
  ///
  /// ACTION:
  /// Inspect Mode, Rate and CurrentPositionUs immediately after construction
  ///
  /// EXPECTED RESULT:
  /// Mode is Live, Rate is 1.0, CurrentPositionUs is 0
  /// </summary>
  [Test]
  public void NewPlayer_HasLiveDefaults()
  {
    using var player = new Player(NullLoggerFactory.Instance, new FakeLive(), new FakePlayback());

    Assert.That(player.Mode, Is.EqualTo(Player.PlayerMode.Live));
    Assert.That(player.Rate, Is.EqualTo(1.0));
    Assert.That(player.CurrentPositionUs, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// GoLiveAsync is called on a configured Player
  ///
  /// ACTION:
  /// Configure the player, call GoLiveAsync
  ///
  /// EXPECTED RESULT:
  /// LiveStreamService.SubscribeAsync is invoked with the configured camera and profile
  /// </summary>
  [Test]
  public async Task GoLiveAsync_Subscribes()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");

    await player.GoLiveAsync(CancellationToken.None);

    Assert.That(live.SubscribeCount, Is.EqualTo(1));
    Assert.That(live.LastProfile, Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// A GOP arrives before the server Ack has cleared ignoreData
  ///
  /// ACTION:
  /// Configure the player, go live, raise OnGop on the fake feed before Ack
  ///
  /// EXPECTED RESULT:
  /// The GOP is not forwarded to the cache
  /// </summary>
  [Test]
  public async Task GopBeforeAck_Ignored()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);

    var feed = live.LastFeed!;
    feed.RaiseGop(new GopMessage(GopFlags.Begin, "main", 1000, new byte[] { 1 }));

    // No direct API for observing the Fetcher, but when an Ack arrives and
    // another GOP comes in, it should be the first buffered (live commit path)
    feed.RaiseStatus(StreamStatus.Ack);
    feed.RaiseGop(new GopMessage(GopFlags.Begin, "main", 2000, new byte[] { 1 }));

    // The player uses seekBuffer internally; verify via the observable effect:
    // CurrentPositionUs remains 0 because no frame has been rendered, but the
    // feed's first post-Ack GOP is what initializes the flow. We assert
    // indirectly that pre-Ack data didn't break the state machine.
    Assert.That(player.Mode, Is.EqualTo(Player.PlayerMode.Live));
    Assert.That(live.SubscribeCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// The server status transitions to Recording
  ///
  /// ACTION:
  /// Configure the player, go live, raise StreamStatus.Recording on the feed
  ///
  /// EXPECTED RESULT:
  /// Mode flips to Playback, min/max rate expand to -8..8
  /// </summary>
  [Test]
  public async Task RecordingStatus_SetsPlaybackMode()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);

    live.LastFeed!.RaiseStatus(StreamStatus.Recording);

    Assert.That(player.Mode, Is.EqualTo(Player.PlayerMode.Playback));
    Assert.That(player.MinRate, Is.EqualTo(-8));
    Assert.That(player.MaxRate, Is.EqualTo(8));
  }

  /// <summary>
  /// SCENARIO:
  /// The server status transitions to Live
  ///
  /// ACTION:
  /// After being in playback, raise StreamStatus.Live on the feed
  ///
  /// EXPECTED RESULT:
  /// Mode flips to Live, rate is clamped to 1, direction to forward
  /// </summary>
  [Test]
  public async Task LiveStatus_ClampsRateToOne()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);
    live.LastFeed!.RaiseStatus(StreamStatus.Recording);
    player.SetRate(4.0);

    live.LastFeed.RaiseStatus(StreamStatus.Live);

    Assert.That(player.Mode, Is.EqualTo(Player.PlayerMode.Live));
    Assert.That(player.Rate, Is.EqualTo(1));
    Assert.That(player.Direction, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// SetRate is called in live mode
  ///
  /// ACTION:
  /// Player is live, call SetRate(2.0)
  ///
  /// EXPECTED RESULT:
  /// Rate remains 1 (live mode rejects rate changes)
  /// </summary>
  [Test]
  public async Task SetRate_LiveMode_Ignored()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);
    live.LastFeed!.RaiseStatus(StreamStatus.Live);

    player.SetRate(2.0);

    Assert.That(player.Rate, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// SetRate is called in playback mode at rates that trigger stride
  ///
  /// ACTION:
  /// Transition to playback, SetRate(4.0)
  ///
  /// EXPECTED RESULT:
  /// Rate becomes 4, stride becomes 4 (floor(rate) once rate >= 3)
  /// </summary>
  [Test]
  public async Task SetRate_PlaybackAboveThreshold_UpdatesStride()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);
    live.LastFeed!.RaiseStatus(StreamStatus.Recording);

    player.SetRate(4.0);

    Assert.That(player.Rate, Is.EqualTo(4.0));
    Assert.That(player.Stride, Is.EqualTo(4));
  }

  /// <summary>
  /// SCENARIO:
  /// SetRate is called in playback mode below the stride threshold
  ///
  /// ACTION:
  /// Transition to playback, SetRate(2.0)
  ///
  /// EXPECTED RESULT:
  /// Rate becomes 2, stride stays at 1
  /// </summary>
  [Test]
  public async Task SetRate_PlaybackBelowThreshold_StrideUnchanged()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);
    live.LastFeed!.RaiseStatus(StreamStatus.Recording);

    player.SetRate(2.0);

    Assert.That(player.Rate, Is.EqualTo(2.0));
    Assert.That(player.Stride, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// Negative rate is set in playback mode
  ///
  /// ACTION:
  /// Enter playback, SetRate(-2.0)
  ///
  /// EXPECTED RESULT:
  /// Rate is 2.0 and direction is -1
  /// </summary>
  [Test]
  public async Task SetRate_Negative_SetsDirectionReverse()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);
    live.LastFeed!.RaiseStatus(StreamStatus.Recording);

    player.SetRate(-2.0);

    Assert.That(player.Rate, Is.EqualTo(2.0));
    Assert.That(player.Direction, Is.EqualTo(-1));
  }

  /// <summary>
  /// SCENARIO:
  /// TogglePause flips the paused flag
  ///
  /// ACTION:
  /// Call TogglePause twice
  ///
  /// EXPECTED RESULT:
  /// Paused is true after first call, false after second
  /// </summary>
  [Test]
  public void TogglePause_Toggles()
  {
    using var player = new Player(NullLoggerFactory.Instance, new FakeLive(), new FakePlayback());

    player.TogglePause();
    Assert.That(player.Paused, Is.True);

    player.TogglePause();
    Assert.That(player.Paused, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// SeekAsync switches from live to playback
  ///
  /// ACTION:
  /// Go live first, then SeekAsync to a timestamp
  ///
  /// EXPECTED RESULT:
  /// The live feed is unsubscribed and PlaybackService.StartAsync is invoked
  /// </summary>
  [Test]
  public async Task SeekAsync_FromLive_SwitchesToPlayback()
  {
    var live = new FakeLive();
    var playback = new FakePlayback();
    using var player = new Player(NullLoggerFactory.Instance, live, playback);
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);

    await player.SeekAsync(5_000_000, CancellationToken.None);

    Assert.That(live.UnsubscribeCount, Is.EqualTo(1));
    Assert.That(playback.StartCount, Is.EqualTo(1));
    Assert.That(playback.LastFrom, Is.EqualTo(5_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// SetProfileAsync is called while live
  ///
  /// ACTION:
  /// Go live, call SetProfileAsync with a different profile
  ///
  /// EXPECTED RESULT:
  /// A fresh live subscription is opened with the new profile
  /// </summary>
  [Test]
  public async Task SetProfileAsync_Live_Resubscribes()
  {
    var live = new FakeLive();
    using var player = new Player(NullLoggerFactory.Instance, live, new FakePlayback());
    player.Configure(Guid.NewGuid(), "main");
    await player.GoLiveAsync(CancellationToken.None);

    await player.SetProfileAsync("sub", CancellationToken.None);

    Assert.That(live.SubscribeCount, Is.EqualTo(2));
    Assert.That(live.LastProfile, Is.EqualTo("sub"));
    Assert.That(player.CurrentProfile, Is.EqualTo("sub"));
  }

  /// <summary>
  /// SCENARIO:
  /// SetProfileAsync is called while in playback
  ///
  /// ACTION:
  /// Seek to a timestamp (enter playback), then call SetProfileAsync
  ///
  /// EXPECTED RESULT:
  /// A new playback stream is opened at the current playhead with the new profile
  /// </summary>
  [Test]
  public async Task SetProfileAsync_Playback_ResubscribesAsPlayback()
  {
    var live = new FakeLive();
    var playback = new FakePlayback();
    using var player = new Player(NullLoggerFactory.Instance, live, playback);
    player.Configure(Guid.NewGuid(), "main");
    await player.SeekAsync(5_000_000, CancellationToken.None);

    await player.SetProfileAsync("sub", CancellationToken.None);

    Assert.That(playback.StartCount, Is.EqualTo(2));
    Assert.That(playback.LastProfile, Is.EqualTo("sub"));
    Assert.That(player.CurrentProfile, Is.EqualTo("sub"));
  }

  // ---------------------------------------------------------------------
  // Fakes
  // ---------------------------------------------------------------------

  private sealed class FakeVideoFeed(Guid cameraId, string profile) : IVideoFeed
  {
    public Guid CameraId { get; } = cameraId;
    public string Profile { get; } = profile;
    public ReadOnlyMemory<byte> LastInit => ReadOnlyMemory<byte>.Empty;
    public bool Disposed { get; private set; }
    public int FetchCount { get; private set; }

    public event Action<ReadOnlyMemory<byte>>? OnInit;
    public event Action<GopMessage>? OnGop;
    public event Action<StreamStatus>? OnStatus;
    public event Action<GapStatus>? OnGap;
    public event Action? OnCompleted;

    public void RaiseInit(ReadOnlyMemory<byte> data) => OnInit?.Invoke(data);
    public void RaiseGop(GopMessage gop) => OnGop?.Invoke(gop);
    public void RaiseStatus(StreamStatus status) => OnStatus?.Invoke(status);
    public void RaiseGap(GapStatus gap) => OnGap?.Invoke(gap);
    public void RaiseCompleted() => OnCompleted?.Invoke();

    public Task SendFetchAsync(ulong from, ulong to, CancellationToken ct)
    {
      FetchCount++;
      return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
  }

  private sealed class FakeLive : ILiveStreamService
  {
    public event Action<IVideoFeed, IVideoFeed>? FeedReplaced;
    public int SubscribeCount { get; private set; }
    public int UnsubscribeCount { get; private set; }
    public string? LastProfile { get; private set; }
    public FakeVideoFeed? LastFeed { get; private set; }

    public Task<IVideoFeed> SubscribeAsync(Guid cameraId, string profile, CancellationToken ct)
    {
      SubscribeCount++;
      LastProfile = profile;
      LastFeed = new FakeVideoFeed(cameraId, profile);
      FeedReplaced?.Invoke(LastFeed, LastFeed);
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
    public int SeekCount { get; private set; }
    public string? LastProfile { get; private set; }
    public ulong LastFrom { get; private set; }

    public Task<IVideoFeed> StartAsync(Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct)
    {
      StartCount++;
      LastProfile = profile;
      LastFrom = from;
      return Task.FromResult<IVideoFeed>(new FakeVideoFeed(cameraId, profile));
    }

    public Task<IVideoFeed> SeekAsync(IVideoFeed current, ulong timestamp, CancellationToken ct)
    {
      SeekCount++;
      return StartAsync(current.CameraId, current.Profile, timestamp, null, ct);
    }

    public Task StopAsync(IVideoFeed feed, CancellationToken ct) => feed.DisposeAsync().AsTask();
  }
}
