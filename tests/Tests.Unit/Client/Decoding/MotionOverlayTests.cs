using System.Buffers.Binary;
using System.IO.Compression;
using Client.Core.Decoding;
using Client.Core.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class MotionOverlayTests
{
  private const long PlayheadUs = 10_000_000;

  /// <summary>
  /// SCENARIO:
  /// The overlay ticks in playback mode with an empty buffer
  ///
  /// ACTION:
  /// Attach a feed, Tick with an unpaused playback view
  ///
  /// EXPECTED RESULT:
  /// A fetch is sent from the playhead with a 30-second window
  /// </summary>
  [Test]
  public void Tick_PlaybackEmptyBuffer_FetchesFromPlayhead()
  {
    var (overlay, feed) = NewOverlay();

    overlay.Tick(PlaybackView(PlayheadUs));

    Assert.Multiple(() =>
    {
      Assert.That(feed.FetchFrom, Is.EqualTo((ulong)PlayheadUs));
      Assert.That(feed.FetchTo, Is.EqualTo((ulong)PlayheadUs + 30_000_000UL));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The overlay ticks at 4x playback
  ///
  /// ACTION:
  /// Tick with rate 4
  ///
  /// EXPECTED RESULT:
  /// The fetch window still starts at the playhead (the fetch itself always
  /// requests 30 seconds; the widened window controls when fetching triggers)
  /// </summary>
  [Test]
  public void Tick_HighRate_StillFetchesFromPlayhead()
  {
    var (overlay, feed) = NewOverlay();

    overlay.Tick(new OverlayPlayerView(PlayheadUs, 4, 1, false, Player.PlayerMode.Playback));

    Assert.That(feed.FetchFrom, Is.EqualTo((ulong)PlayheadUs));
  }

  /// <summary>
  /// SCENARIO:
  /// The overlay's feed is live (server sent the Live status)
  ///
  /// ACTION:
  /// Raise Live, then Tick unpaused
  ///
  /// EXPECTED RESULT:
  /// No fetch is sent (live data is pushed; the fetcher gates itself)
  /// </summary>
  [Test]
  public void Tick_LiveFeed_DoesNotFetch()
  {
    var (overlay, feed) = NewOverlay();
    feed.RaiseStatus(StreamStatus.Live);

    overlay.Tick(new OverlayPlayerView(PlayheadUs, 1, 1, false, Player.PlayerMode.Live));

    Assert.That(feed.FetchFrom, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// The overlay ticks while the player is paused
  ///
  /// ACTION:
  /// Tick with a paused playback view
  ///
  /// EXPECTED RESULT:
  /// No fetch is sent (scrubbing renders from cache only)
  /// </summary>
  [Test]
  public void Tick_Paused_DoesNotFetch()
  {
    var (overlay, feed) = NewOverlay();

    overlay.Tick(new OverlayPlayerView(PlayheadUs, 1, 1, true, Player.PlayerMode.Playback));

    Assert.That(feed.FetchFrom, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// A motion GOP arrives for the playhead position
  ///
  /// ACTION:
  /// Tick to establish the view, then raise a Begin GOP containing a sync unit
  ///
  /// EXPECTED RESULT:
  /// FrameChanged fires with the decoded frame
  /// </summary>
  [Test]
  public void GopArrival_AtPlayhead_PublishesFrame()
  {
    var (overlay, feed) = NewOverlay();
    MotionFrame? published = null;
    overlay.FrameChanged += f => published = f;
    overlay.Tick(PlaybackView(PlayheadUs));

    feed.RaiseGop(new GopMessage(GopFlags.Begin | GopFlags.End, "m", (ulong)PlayheadUs,
      BuildUnit((ulong)PlayheadUs, 2, 1, [7, 9], sync: true)));

    Assert.That(published, Is.Not.Null);
    Assert.That(published!.Cells, Is.EqualTo(new byte[] { 7, 9 }));
  }

  /// <summary>
  /// SCENARIO:
  /// The playhead moves beyond the 5-second hold limit of the painted frame
  ///
  /// ACTION:
  /// Publish a frame at the playhead, then Tick 6 seconds later
  ///
  /// EXPECTED RESULT:
  /// FrameChanged fires with null to clear the stale grid
  /// </summary>
  [Test]
  public void Tick_BeyondHoldLimit_ClearsFrame()
  {
    var (overlay, feed) = NewOverlay();
    var events = new List<MotionFrame?>();
    overlay.FrameChanged += events.Add;
    overlay.Tick(PlaybackView(PlayheadUs));
    feed.RaiseGop(new GopMessage(GopFlags.Begin | GopFlags.End, "m", (ulong)PlayheadUs,
      BuildUnit((ulong)PlayheadUs, 2, 1, [7, 9], sync: true)));

    overlay.Tick(PlaybackView(PlayheadUs + 6_000_000));

    Assert.That(events, Has.Count.EqualTo(2));
    Assert.That(events[^1], Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// OnSeek resets the overlay after its feed went live
  ///
  /// ACTION:
  /// Raise Live, OnSeek, then Tick with a playback view
  ///
  /// EXPECTED RESULT:
  /// The live gate is cleared by the reset, so the tick fetches again
  /// </summary>
  [Test]
  public void OnSeek_AfterLive_ReenablesFetching()
  {
    var (overlay, feed) = NewOverlay();
    feed.RaiseStatus(StreamStatus.Live);

    overlay.OnSeek();
    overlay.Tick(PlaybackView(PlayheadUs));

    Assert.That(feed.FetchFrom, Is.EqualTo((ulong)PlayheadUs));
  }

  /// <summary>
  /// SCENARIO:
  /// DetachFeed is called while a frame is painted
  ///
  /// ACTION:
  /// Publish a frame, DetachFeed, Tick again
  ///
  /// EXPECTED RESULT:
  /// Detach publishes a null frame and later ticks neither fetch nor publish
  /// </summary>
  [Test]
  public void DetachFeed_ClearsAndStops()
  {
    var (overlay, feed) = NewOverlay();
    var events = new List<MotionFrame?>();
    overlay.FrameChanged += events.Add;
    overlay.Tick(PlaybackView(PlayheadUs));
    feed.RaiseGop(new GopMessage(GopFlags.Begin | GopFlags.End, "m", (ulong)PlayheadUs,
      BuildUnit((ulong)PlayheadUs, 2, 1, [7, 9], sync: true)));

    overlay.DetachFeed();
    var fetchAfterDetach = feed.FetchFrom;
    overlay.Tick(PlaybackView(PlayheadUs + 1_000_000));

    Assert.Multiple(() =>
    {
      Assert.That(events[^1], Is.Null);
      Assert.That(feed.FetchFrom, Is.EqualTo(fetchAfterDetach));
    });
  }

  private static OverlayPlayerView PlaybackView(long ts) =>
    new(ts, 1, 1, false, Player.PlayerMode.Playback);

  private static (MotionOverlay Overlay, FakeVideoFeed Feed) NewOverlay()
  {
    var overlay = new MotionOverlay(NullLogger.Instance);
    var feed = new FakeVideoFeed();
    overlay.AttachFeed(feed);
    return (overlay, feed);
  }

  private static byte[] BuildUnit(ulong timestamp, ushort cols, ushort rows, byte[] cells, bool sync)
  {
    using var compressedStream = new MemoryStream();
    using (var deflate = new DeflateStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
      deflate.Write(cells);
    var compressed = compressedStream.ToArray();

    var unit = new byte[22 + compressed.Length];
    unit[0] = (byte)'M';
    unit[1] = (byte)'G';
    unit[2] = (byte)'R';
    unit[3] = (byte)'D';
    unit[4] = 1;
    unit[5] = (byte)(sync ? 1 : 0);
    BinaryPrimitives.WriteUInt64LittleEndian(unit.AsSpan(6), timestamp);
    BinaryPrimitives.WriteUInt16LittleEndian(unit.AsSpan(14), cols);
    BinaryPrimitives.WriteUInt16LittleEndian(unit.AsSpan(16), rows);
    BinaryPrimitives.WriteUInt32LittleEndian(unit.AsSpan(18), (uint)compressed.Length);
    compressed.CopyTo(unit, 22);
    return unit;
  }

  private sealed class FakeVideoFeed : IVideoFeed
  {
    public Guid CameraId => Guid.Empty;
    public string Profile => "m";
    public ReadOnlyMemory<byte> LastInit => ReadOnlyMemory<byte>.Empty;
    public ulong FetchFrom { get; private set; }
    public ulong FetchTo { get; private set; }

    public event Action<ReadOnlyMemory<byte>>? OnInit;
    public event Action<GopMessage>? OnGop;
    public event Action<StreamStatus>? OnStatus;
    public event Action<GapStatus>? OnGap;
    public event Action? OnCompleted;

    public void RaiseGop(GopMessage gop) => OnGop?.Invoke(gop);
    public void RaiseStatus(StreamStatus s) => OnStatus?.Invoke(s);
    public void RaiseGap(GapStatus g) => OnGap?.Invoke(g);
    public void RaiseCompleted() => OnCompleted?.Invoke();
    public void RaiseInit(ReadOnlyMemory<byte> data) => OnInit?.Invoke(data);

    public void Start() { }

    public Task SendFetchAsync(ulong from, ulong to, CancellationToken ct)
    {
      FetchFrom = from;
      FetchTo = to;
      return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
