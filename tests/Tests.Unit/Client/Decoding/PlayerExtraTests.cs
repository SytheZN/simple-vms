using Client.Core.Decoding;
using Client.Core.Decoding.Diagnostics;
using Client.Core.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;

namespace Tests.Unit.Client.Decoding;

/// <summary>
/// Coverage for Player surface area not exercised by PlayerTests:
/// the unfired-event defaults, Detach/Stop/Dispose lifecycle, scrub flow,
/// and the BackendDisplayName / Renderer / Diagnostics handles.
/// </summary>
[TestFixture]
public class PlayerExtraTests
{
  private static Player NewPlayer(ILiveStreamService? live = null, IPlaybackService? playback = null)
  {
    var pipeline = new DecodePipelineFactory(NullLoggerFactory.Instance).Create(DecodeRole.Main)!.Value;
    return new Player(NullLoggerFactory.Instance, pipeline.Backend, pipeline.Renderer,
      live ?? new FakeLive(), playback ?? new FakePlayback(), new DiagnosticsSettings());
  }

  /// <summary>
  /// SCENARIO:
  /// A fresh Player has not been told about a stream's playback range
  ///
  /// ACTION:
  /// Read MinRate, MaxRate, CurrentProfile, Stride, Direction, Buffering, Paused
  ///
  /// EXPECTED RESULT:
  /// Defaults: MinRate=1, MaxRate=1, profile is "main", stride=1, direction=1,
  /// not buffering, not paused
  /// </summary>
  [Test]
  public void Defaults_AreLiveSafe()
  {
    using var p = NewPlayer();

    Assert.Multiple(() =>
    {
      Assert.That(p.MinRate, Is.EqualTo(1));
      Assert.That(p.MaxRate, Is.EqualTo(1));
      Assert.That(p.CurrentProfile, Is.EqualTo("main"));
      Assert.That(p.Stride, Is.EqualTo(1));
      Assert.That(p.Direction, Is.EqualTo(1));
      Assert.That(p.Buffering, Is.False);
      Assert.That(p.Paused, Is.False);
      Assert.That(p.Renderer, Is.Not.Null);
      Assert.That(p.Diagnostics, Is.Not.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Configure stores camera id and profile so the next subscribe carries them
  ///
  /// ACTION:
  /// Configure with profile "sub", read CurrentProfile
  ///
  /// EXPECTED RESULT:
  /// CurrentProfile reflects the configured profile
  /// </summary>
  [Test]
  public void Configure_SetsCurrentProfile()
  {
    using var p = NewPlayer();

    p.Configure(Guid.NewGuid(), "sub");

    Assert.That(p.CurrentProfile, Is.EqualTo("sub"));
  }

  /// <summary>
  /// SCENARIO:
  /// DetachAsync runs against a player that never subscribed
  ///
  /// ACTION:
  /// Call DetachAsync without prior GoLive/Seek
  ///
  /// EXPECTED RESULT:
  /// Completes without exception; live service sees no unsubscribe
  /// </summary>
  [Test]
  public async Task DetachAsync_NoFeed_NoOp()
  {
    var live = new FakeLive();
    using var p = NewPlayer(live);

    await p.DetachAsync();

    Assert.That(live.UnsubscribeCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// DetachAsync against an active live feed unsubscribes and resets state
  ///
  /// ACTION:
  /// Configure, GoLive, then DetachAsync
  ///
  /// EXPECTED RESULT:
  /// Live service sees one unsubscribe; rate/direction reset; not paused
  /// </summary>
  [Test]
  public async Task DetachAsync_LiveFeed_UnsubscribesAndResets()
  {
    var live = new FakeLive();
    using var p = NewPlayer(live);
    p.Configure(Guid.NewGuid(), "main");
    await p.GoLiveAsync(CancellationToken.None);

    await p.DetachAsync();

    Assert.Multiple(() =>
    {
      Assert.That(live.UnsubscribeCount, Is.EqualTo(1));
      Assert.That(p.Rate, Is.EqualTo(1));
      Assert.That(p.Direction, Is.EqualTo(1));
      Assert.That(p.Paused, Is.False);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// TogglePause emits PausedChanged with the new state
  ///
  /// ACTION:
  /// Subscribe to PausedChanged, call TogglePause twice
  ///
  /// EXPECTED RESULT:
  /// Event fires twice: true then false
  /// </summary>
  [Test]
  public void TogglePause_EmitsPausedChanged()
  {
    using var p = NewPlayer();
    var events = new List<bool>();
    p.PausedChanged += events.Add;

    p.TogglePause();
    p.TogglePause();

    Assert.That(events, Is.EqualTo(new[] { true, false }));
  }

  /// <summary>
  /// SCENARIO:
  /// SetRate in playback mode at a value different from current rate
  ///
  /// ACTION:
  /// Enter playback, subscribe to RateChanged, SetRate(2)
  ///
  /// EXPECTED RESULT:
  /// RateChanged fires once with 2
  /// </summary>
  [Test]
  public async Task SetRate_DifferentValue_EmitsRateChanged()
  {
    var live = new FakeLive();
    using var p = NewPlayer(live);
    p.Configure(Guid.NewGuid(), "main");
    await p.GoLiveAsync(CancellationToken.None);
    live.LastFeed!.RaiseStatus(StreamStatus.Recording);

    var events = new List<double>();
    p.RateChanged += events.Add;

    p.SetRate(2.0);

    Assert.That(events, Is.EqualTo(new[] { 2.0 }));
  }

  /// <summary>
  /// SCENARIO:
  /// SetRate in playback mode at the current rate value
  ///
  /// ACTION:
  /// Enter playback, SetRate(1) (default), subscribe, SetRate(1) again
  ///
  /// EXPECTED RESULT:
  /// RateChanged does not fire on the redundant call
  /// </summary>
  [Test]
  public async Task SetRate_SameValue_DoesNotEmit()
  {
    var live = new FakeLive();
    using var p = NewPlayer(live);
    p.Configure(Guid.NewGuid(), "main");
    await p.GoLiveAsync(CancellationToken.None);
    live.LastFeed!.RaiseStatus(StreamStatus.Recording);

    var fired = false;
    p.RateChanged += _ => fired = true;

    p.SetRate(1.0);

    Assert.That(fired, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// ScrubStart pauses the player without firing PausedChanged through TogglePause
  ///
  /// ACTION:
  /// Call ScrubStart on a fresh (un-paused) player
  ///
  /// EXPECTED RESULT:
  /// Paused becomes true (set directly, not via toggle - PausedChanged is not
  /// emitted because the scrub flow ends with ScrubEndAsync->SeekAsync which
  /// itself doesn't toggle the flag externally)
  /// </summary>
  [Test]
  public void ScrubStart_SetsPaused()
  {
    using var p = NewPlayer();

    p.ScrubStart();

    Assert.That(p.Paused, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// ScrubEndAsync exits scrub state by un-pausing and seeking
  ///
  /// ACTION:
  /// ScrubStart, then ScrubEndAsync to a timestamp
  ///
  /// EXPECTED RESULT:
  /// Paused becomes false; PlaybackService.StartAsync was called once
  /// (Seek path); LastFrom matches the requested timestamp
  /// </summary>
  [Test]
  public async Task ScrubEndAsync_UnpausesAndSeeks()
  {
    var playback = new FakePlayback();
    using var p = NewPlayer(playback: playback);
    p.Configure(Guid.NewGuid(), "main");
    p.ScrubStart();

    await p.ScrubEndAsync(2_500_000, CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(p.Paused, Is.False);
      Assert.That(playback.StartCount, Is.EqualTo(1));
      Assert.That(playback.LastFrom, Is.EqualTo(2_500_000UL));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Stop on a never-subscribed player resets rate, direction, paused
  ///
  /// ACTION:
  /// Call Stop without prior subscribe
  ///
  /// EXPECTED RESULT:
  /// No exception; rate=1, direction=1
  /// </summary>
  [Test]
  public void Stop_NoSubscribe_ResetsState()
  {
    using var p = NewPlayer();

    p.Stop();

    Assert.Multiple(() =>
    {
      Assert.That(p.Rate, Is.EqualTo(1));
      Assert.That(p.Direction, Is.EqualTo(1));
      Assert.That(p.Paused, Is.False);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Dispose is called more than once
  ///
  /// ACTION:
  /// Configure, GoLive, Dispose, Dispose
  ///
  /// EXPECTED RESULT:
  /// Second Dispose is a no-op (no exception)
  /// </summary>
  [Test]
  public async Task Dispose_Twice_Idempotent()
  {
    var p = NewPlayer();
    p.Configure(Guid.NewGuid(), "main");
    await p.GoLiveAsync(CancellationToken.None);

    p.Dispose();

    Assert.DoesNotThrow(() => p.Dispose());
  }

  private sealed class FakeVideoFeed(Guid cameraId, string profile) : IVideoFeed
  {
    public Guid CameraId { get; } = cameraId;
    public string Profile { get; } = profile;
    public ReadOnlyMemory<byte> LastInit => ReadOnlyMemory<byte>.Empty;

    public event Action<ReadOnlyMemory<byte>>? OnInit;
    public event Action<GopMessage>? OnGop;
    public event Action<StreamStatus>? OnStatus;
    public event Action<GapStatus>? OnGap;
    public event Action? OnCompleted;

    public void RaiseInit(ReadOnlyMemory<byte> d) => OnInit?.Invoke(d);
    public void RaiseGop(GopMessage g) => OnGop?.Invoke(g);
    public void RaiseStatus(StreamStatus s) => OnStatus?.Invoke(s);
    public void RaiseGap(GapStatus g) => OnGap?.Invoke(g);
    public void RaiseCompleted() => OnCompleted?.Invoke();

    public Task SendFetchAsync(ulong from, ulong to, CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FakeLive : ILiveStreamService
  {
    public event Action<IVideoFeed, IVideoFeed>? FeedReplaced;
    public int SubscribeCount { get; private set; }
    public int UnsubscribeCount { get; private set; }
    public FakeVideoFeed? LastFeed { get; private set; }

    public Task<IVideoFeed> SubscribeAsync(Guid cameraId, string profile, CancellationToken ct)
    {
      SubscribeCount++;
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
    public ulong LastFrom { get; private set; }

    public Task<IVideoFeed> StartAsync(Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct)
    {
      StartCount++;
      LastFrom = from;
      return Task.FromResult<IVideoFeed>(new FakeVideoFeed(cameraId, profile));
    }

    public Task<IVideoFeed> SeekAsync(IVideoFeed current, ulong timestamp, CancellationToken ct) =>
      StartAsync(current.CameraId, current.Profile, timestamp, null, ct);

    public Task StopAsync(IVideoFeed feed, CancellationToken ct) => feed.DisposeAsync().AsTask();
  }
}
