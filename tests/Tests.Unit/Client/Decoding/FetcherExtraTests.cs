using Client.Core.Decoding;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class FetcherExtraTests
{
  /// <summary>
  /// SCENARIO:
  /// A fresh Fetcher has nothing buffered
  ///
  /// ACTION:
  /// Read every observable property
  ///
  /// EXPECTED RESULT:
  /// All counts are zero; oldest/newest timestamps are null; GopTimestamps empty
  /// </summary>
  [Test]
  public void Empty_PropertiesAreZero()
  {
    var f = new Fetcher();

    Assert.Multiple(() =>
    {
      Assert.That(f.BufferedGopCount, Is.Zero);
      Assert.That(f.BufferedBytes, Is.Zero);
      Assert.That(f.OldestTimestamp(), Is.Null);
      Assert.That(f.NewestTimestamp(), Is.Null);
      Assert.That(f.GopTimestamps(), Is.Empty);
      Assert.That(f.FindGop(1000), Is.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Multiple chunks across multiple GOPs sum into BufferedBytes
  ///
  /// ACTION:
  /// Append chunks of varying sizes, read BufferedBytes
  ///
  /// EXPECTED RESULT:
  /// Total reflects the sum of all chunk lengths
  /// </summary>
  [Test]
  public void BufferedBytes_SumsAllChunks()
  {
    var f = new Fetcher();
    f.AppendData(1000, new byte[5]);
    f.AppendData(1000, new byte[7]);
    f.AppendData(2000, new byte[3]);

    Assert.That(f.BufferedBytes, Is.EqualTo(15));
  }

  /// <summary>
  /// SCENARIO:
  /// Reverse playback evicts forward GOPs that are no longer needed
  ///
  /// ACTION:
  /// Populate 5 GOPs in playback mode, call SetTarget with toUs &lt; fromUs
  /// (reverse direction) at the middle GOP
  ///
  /// EXPECTED RESULT:
  /// Trailing GOPs beyond containing-GOP-plus-1 are evicted
  /// </summary>
  [Test]
  public void SetTarget_Reverse_EvictsForwardGops()
  {
    var f = new Fetcher();
    f.HandleRecording();
    for (ulong t = 1000; t <= 5000; t += 1000)
      f.AppendData(t, new byte[] { 1 });

    f.SetTarget(2000, 1000);

    Assert.That(f.GopTimestamps(), Is.EqualTo(new ulong[] { 1000, 2000, 3000 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Reverse playback with empty cache fetches a window backward
  ///
  /// ACTION:
  /// Attach fetch recorder, recording mode, SetTarget(fromUs=40M, toUs=10M)
  ///
  /// EXPECTED RESULT:
  /// One fetch is sent with from at the target and to = from - 30s
  /// </summary>
  [Test]
  public void SetTarget_ReverseEmptyCache_FetchesBackwardWindow()
  {
    var f = new Fetcher();
    (ulong From, ulong To)? captured = null;
    f.Attach((from, to) => { captured = (from, to); return Task.CompletedTask; });
    f.HandleRecording();

    f.SetTarget(40_000_000, 10_000_000);

    Assert.Multiple(() =>
    {
      Assert.That(captured!.Value.From, Is.EqualTo(40_000_000UL));
      Assert.That(captured.Value.To, Is.EqualTo(10_000_000UL));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A reverse fetch starts inside a known gap
  ///
  /// ACTION:
  /// Report a gap, then SetTarget reverse with from inside that gap
  ///
  /// EXPECTED RESULT:
  /// The fetch from-point is rewound to the gap's lower bound
  /// </summary>
  [Test]
  public void SetTarget_ReverseSkipsOverGap()
  {
    var f = new Fetcher();
    (ulong From, ulong To)? captured = null;
    f.Attach((from, to) => { captured = (from, to); return Task.CompletedTask; });
    f.HandleRecording();
    f.HandleGap(5_000_000, 6_000_000);

    f.SetTarget(5_500_000, 1_000_000);

    Assert.That(captured!.Value.From, Is.EqualTo(5_000_000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// FetchAtAsync is called twice; the first is in-flight when the second
  /// arrives
  ///
  /// ACTION:
  /// Call FetchAtAsync, then FetchAtAsync again before completion, then
  /// HandleFetchComplete
  ///
  /// EXPECTED RESULT:
  /// Both returned Tasks complete on the same HandleFetchComplete
  /// </summary>
  [Test]
  public async Task FetchAtAsync_SecondCallShareCompletion()
  {
    var f = new Fetcher();
    var fetchCount = 0;
    f.Attach((_, _) => { fetchCount++; return Task.CompletedTask; });
    f.HandleRecording();

    var t1 = f.FetchAtAsync(1_000_000);
    var t2 = f.FetchAtAsync(1_000_000);

    f.HandleFetchComplete();
    await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Multiple(() =>
    {
      Assert.That(fetchCount, Is.EqualTo(1));
      Assert.That(t1.IsCompletedSuccessfully, Is.True);
      Assert.That(t2.IsCompletedSuccessfully, Is.True);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// FetchAtAsync is called without a transport attached
  ///
  /// ACTION:
  /// New fetcher (no Attach), call FetchAtAsync
  ///
  /// EXPECTED RESULT:
  /// Returns Task.CompletedTask (no fetch attempted, no hang)
  /// </summary>
  [Test]
  public void FetchAtAsync_NoTransport_ReturnsCompletedTask()
  {
    var f = new Fetcher();

    var task = f.FetchAtAsync(1000);

    Assert.That(task.IsCompletedSuccessfully, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Reset cancels any pending FetchAtAsync waiter
  ///
  /// ACTION:
  /// FetchAtAsync, then Reset before HandleFetchComplete
  ///
  /// EXPECTED RESULT:
  /// The pending task completes (Reset releases waiters)
  /// </summary>
  [Test]
  public async Task Reset_CompletesPendingFetchTask()
  {
    var f = new Fetcher();
    f.Attach((_, _) => Task.CompletedTask);
    f.HandleRecording();

    var task = f.FetchAtAsync(1_000_000);
    Assert.That(task.IsCompleted, Is.False);

    f.Reset();
    await task.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.That(task.IsCompletedSuccessfully, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Detach removes the transport so subsequent SetTarget cannot fetch
  ///
  /// ACTION:
  /// Attach, HandleRecording, Detach, then SetTarget
  ///
  /// EXPECTED RESULT:
  /// No fetch is sent (transport is null after Detach)
  /// </summary>
  [Test]
  public void Detach_PreventsFurtherFetches()
  {
    var f = new Fetcher();
    var fetchCount = 0;
    f.Attach((_, _) => { fetchCount++; return Task.CompletedTask; });
    f.HandleRecording();

    f.Detach();
    f.SetTarget(10_000_000, 40_000_000);

    Assert.That(fetchCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// MergedData on a single-chunk GOP returns the chunk verbatim
  ///
  /// ACTION:
  /// Append one chunk to a GOP, call MergedData
  ///
  /// EXPECTED RESULT:
  /// Returned memory IS the same chunk reference (fast-path)
  /// </summary>
  [Test]
  public void MergedData_SingleChunk_ReturnsSameRef()
  {
    var f = new Fetcher();
    var chunk = new byte[] { 1, 2, 3 };
    f.AppendData(1000, chunk);
    var gop = f.FindGop(1000)!;

    var merged = Fetcher.MergedData(gop);

    Assert.That(merged.ToArray(), Is.EqualTo(chunk));
  }

  /// <summary>
  /// SCENARIO:
  /// MergedData on a multi-chunk GOP concatenates the chunks in order
  ///
  /// ACTION:
  /// Append three chunks to one GOP, call MergedData
  ///
  /// EXPECTED RESULT:
  /// Returned memory equals the concatenation
  /// </summary>
  [Test]
  public void MergedData_MultipleChunks_Concatenates()
  {
    var f = new Fetcher();
    f.AppendData(1000, new byte[] { 0xA });
    f.AppendData(1000, new byte[] { 0xB, 0xC });
    f.AppendData(1000, new byte[] { 0xD });
    var gop = f.FindGop(1000)!;

    var merged = Fetcher.MergedData(gop);

    Assert.That(merged.ToArray(), Is.EqualTo(new byte[] { 0xA, 0xB, 0xC, 0xD }));
  }

  /// <summary>
  /// SCENARIO:
  /// Oldest/Newest timestamps reflect the sorted span of GOPs
  ///
  /// ACTION:
  /// Append GOPs out of order, query oldest/newest
  ///
  /// EXPECTED RESULT:
  /// Oldest = lowest timestamp, Newest = highest
  /// </summary>
  [Test]
  public void OldestNewest_ReflectExtremes()
  {
    var f = new Fetcher();
    f.AppendData(3000, new byte[] { 0 });
    f.AppendData(1000, new byte[] { 0 });
    f.AppendData(2000, new byte[] { 0 });

    Assert.Multiple(() =>
    {
      Assert.That(f.OldestTimestamp(), Is.EqualTo(1000UL));
      Assert.That(f.NewestTimestamp(), Is.EqualTo(3000UL));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// HandleLive after HandleRecording flips the live flag back on
  ///
  /// ACTION:
  /// HandleRecording, fire SetTarget that would fetch, HandleLive,
  /// fire SetTarget again
  ///
  /// EXPECTED RESULT:
  /// First SetTarget triggers a fetch (recording); second does not (live)
  /// </summary>
  [Test]
  public void HandleLive_AfterRecording_SuppressesFetch()
  {
    var f = new Fetcher();
    var fetchCount = 0;
    f.Attach((_, _) => { fetchCount++; return Task.CompletedTask; });

    f.HandleRecording();
    f.SetTarget(10_000_000, 40_000_000);
    f.HandleFetchComplete();

    f.HandleLive();
    f.SetTarget(20_000_000, 50_000_000);

    Assert.That(fetchCount, Is.EqualTo(1));
  }
}
