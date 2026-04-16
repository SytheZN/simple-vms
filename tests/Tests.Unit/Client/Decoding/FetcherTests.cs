using Client.Core.Decoding;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class FetcherTests
{
  /// <summary>
  /// SCENARIO:
  /// A new GOP with a fresh timestamp is received
  ///
  /// ACTION:
  /// Call AppendData with the timestamp and chunk bytes
  ///
  /// EXPECTED RESULT:
  /// A single entry is stored with one chunk
  /// </summary>
  [Test]
  public void AppendData_NewGop_AddsEntry()
  {
    var cache = new Fetcher();
    cache.AppendData(1000, new byte[] { 1, 2, 3 });

    var gop = cache.FindGop(1000);
    Assert.That(gop, Is.Not.Null);
    Assert.That(gop!.Chunks, Has.Count.EqualTo(1));
    Assert.That(gop.Timestamp, Is.EqualTo(1000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// A GOP arrives in multiple chunks sharing the same timestamp
  ///
  /// ACTION:
  /// Call AppendData twice with the same timestamp and different payloads
  ///
  /// EXPECTED RESULT:
  /// Both chunks are appended to the same GOP entry
  /// </summary>
  [Test]
  public void AppendData_SameTimestamp_AppendsChunk()
  {
    var cache = new Fetcher();
    cache.AppendData(1000, new byte[] { 1 });
    cache.AppendData(1000, new byte[] { 2 });

    var gop = cache.FindGop(1000);
    Assert.That(gop!.Chunks, Has.Count.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// FindGop is called with a timestamp between two stored GOPs
  ///
  /// ACTION:
  /// Add GOPs at 1000 and 2000, call FindGop(1500)
  ///
  /// EXPECTED RESULT:
  /// Returns the GOP at 1000 (the greatest timestamp that does not exceed 1500)
  /// </summary>
  [Test]
  public void FindGop_BetweenEntries_ReturnsLowerOrEqual()
  {
    var cache = new Fetcher();
    cache.AppendData(1000, new byte[] { 1 });
    cache.AppendData(2000, new byte[] { 2 });

    var gop = cache.FindGop(1500);
    Assert.That(gop!.Timestamp, Is.EqualTo(1000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// FindGop is called with a timestamp earlier than any stored GOP
  ///
  /// ACTION:
  /// Add GOP at 2000, call FindGop(1000)
  ///
  /// EXPECTED RESULT:
  /// Returns null because no GOP can contain the timestamp
  /// </summary>
  [Test]
  public void FindGop_BeforeFirstEntry_ReturnsNull()
  {
    var cache = new Fetcher();
    cache.AppendData(2000, new byte[] { 1 });

    var gop = cache.FindGop(1000);
    Assert.That(gop, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// GOPs are appended out of timestamp order
  ///
  /// ACTION:
  /// Append GOPs at 3000, 1000, 2000 and query GopTimestamps
  ///
  /// EXPECTED RESULT:
  /// The returned list is sorted in ascending order
  /// </summary>
  [Test]
  public void GopTimestamps_ReturnsSorted()
  {
    var cache = new Fetcher();
    cache.AppendData(3000, new byte[] { 1 });
    cache.AppendData(1000, new byte[] { 1 });
    cache.AppendData(2000, new byte[] { 1 });

    Assert.That(cache.GopTimestamps(), Is.EqualTo(new ulong[] { 1000, 2000, 3000 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Playhead moves forward past several cached GOPs
  ///
  /// ACTION:
  /// Populate 5 GOPs (1000..5000) in live mode, call SetTarget(4000, ...)
  ///
  /// EXPECTED RESULT:
  /// GOPs before containing-GOP-minus-1 are evicted, leaving 3000, 4000, 5000
  /// </summary>
  [Test]
  public void SetTarget_Forward_EvictsOldGops()
  {
    var cache = new Fetcher();
    cache.HandleLive();
    for (ulong t = 1000; t <= 5000; t += 1000)
      cache.AppendData(t, new byte[] { 1 });

    cache.SetTarget(4000, 4000 + 30_000_000);

    Assert.That(cache.GopTimestamps(), Is.EqualTo(new ulong[] { 3000, 4000, 5000 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Live mode is active and SetTarget is called
  ///
  /// ACTION:
  /// Attach a fetch recorder, call HandleLive, then SetTarget
  ///
  /// EXPECTED RESULT:
  /// No fetch request is sent (data arrives unprompted in live mode)
  /// </summary>
  [Test]
  public void SetTarget_LiveMode_DoesNotFetch()
  {
    var cache = new Fetcher();
    var fetchCount = 0;
    cache.Attach((from, to) => { fetchCount++; return Task.CompletedTask; });
    cache.HandleLive();

    cache.SetTarget(1000, 2000);

    Assert.That(fetchCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// Playback mode with an empty cache and SetTarget pointing forward
  ///
  /// ACTION:
  /// Attach a fetch recorder, call HandleRecording, then SetTarget
  ///
  /// EXPECTED RESULT:
  /// One fetch is sent covering the forward window starting at the target
  /// </summary>
  [Test]
  public void SetTarget_PlaybackEmptyCache_FetchesWindow()
  {
    var cache = new Fetcher();
    (ulong From, ulong To)? captured = null;
    cache.Attach((from, to) => { captured = (from, to); return Task.CompletedTask; });
    cache.HandleRecording();

    cache.SetTarget(10_000_000, 10_000_000 + 30_000_000);

    Assert.That(captured, Is.Not.Null);
    Assert.That(captured!.Value.From, Is.EqualTo(10_000_000UL));
    Assert.That(captured.Value.To, Is.EqualTo(10_000_000UL + 30_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// SetTarget is called twice while the first fetch is still in flight
  ///
  /// ACTION:
  /// Call SetTarget twice in succession without HandleFetchComplete
  ///
  /// EXPECTED RESULT:
  /// Only the first fetch is sent; the second is suppressed
  /// </summary>
  [Test]
  public void SetTarget_FetchInFlight_DoesNotDuplicate()
  {
    var cache = new Fetcher();
    var fetchCount = 0;
    cache.Attach((from, to) => { fetchCount++; return Task.CompletedTask; });
    cache.HandleRecording();

    cache.SetTarget(10_000_000, 40_000_000);
    cache.SetTarget(11_000_000, 41_000_000);

    Assert.That(fetchCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A fetch completes and a subsequent SetTarget runs
  ///
  /// ACTION:
  /// Fire SetTarget, call HandleFetchComplete, fire SetTarget again
  ///
  /// EXPECTED RESULT:
  /// The second fetch is sent (in-flight flag is cleared on completion)
  /// </summary>
  [Test]
  public void HandleFetchComplete_ClearsInFlight()
  {
    var cache = new Fetcher();
    var fetchCount = 0;
    cache.Attach((from, to) => { fetchCount++; return Task.CompletedTask; });
    cache.HandleRecording();

    cache.SetTarget(10_000_000, 40_000_000);
    cache.HandleFetchComplete();
    cache.SetTarget(20_000_000, 50_000_000);

    Assert.That(fetchCount, Is.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// A gap is reported and a subsequent fetch would start inside the gap
  ///
  /// ACTION:
  /// Call HandleGap(5_000_000, 6_000_000), then SetTarget starting at 5_500_000
  ///
  /// EXPECTED RESULT:
  /// The fetch is advanced to the end of the gap (6_000_000)
  /// </summary>
  [Test]
  public void SetTarget_SkipsOverGap()
  {
    var cache = new Fetcher();
    (ulong From, ulong To)? captured = null;
    cache.Attach((from, to) => { captured = (from, to); return Task.CompletedTask; });
    cache.HandleRecording();
    cache.HandleGap(5_000_000, 6_000_000);

    cache.SetTarget(5_500_000, 5_500_000 + 30_000_000);

    Assert.That(captured!.Value.From, Is.EqualTo(6_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// A one-shot fetch at a scrub target is issued
  ///
  /// ACTION:
  /// Call FetchAtAsync, then HandleFetchComplete
  ///
  /// EXPECTED RESULT:
  /// The task returned by FetchAtAsync completes successfully
  /// </summary>
  [Test]
  public async Task FetchAtAsync_CompletesOnFetchComplete()
  {
    var cache = new Fetcher();
    cache.Attach((from, to) => Task.CompletedTask);
    cache.HandleRecording();

    var task = cache.FetchAtAsync(1_000_000);
    Assert.That(task.IsCompleted, Is.False);

    cache.HandleFetchComplete();
    await task.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.That(task.IsCompletedSuccessfully, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// The cache is populated and then reset
  ///
  /// ACTION:
  /// Append GOPs, call Reset
  ///
  /// EXPECTED RESULT:
  /// GopTimestamps is empty
  /// </summary>
  [Test]
  public void Reset_ClearsGops()
  {
    var cache = new Fetcher();
    cache.AppendData(1000, new byte[] { 1 });
    cache.AppendData(2000, new byte[] { 2 });

    cache.Reset();

    Assert.That(cache.GopTimestamps(), Is.Empty);
  }
}
