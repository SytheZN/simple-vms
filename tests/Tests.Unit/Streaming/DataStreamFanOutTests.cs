using Server.Streaming;
using Shared.Models.Formats;

namespace Tests.Unit.Streaming;

[TestFixture]
public class DataStreamFanOutTests
{
  private static StreamInfo TestInfo => new() { DataFormat = "h264" };

  private static H264NalUnit MakeUnit(ulong ts, bool sync = false) => new()
  {
    Data = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65 },
    Timestamp = ts,
    MediaTimestamp = ts,
    IsSyncPoint = sync,
    IsHeader = false,
    NalType = sync ? H264NalType.Idr : H264NalType.Slice
  };

  private static H264NalUnit MakeHeader(ulong ts, H264NalType nalType) => new()
  {
    Data = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67 },
    Timestamp = ts,
    MediaTimestamp = ts,
    IsSyncPoint = false,
    IsHeader = true,
    NalType = nalType
  };

  private static async Task<List<ulong>> Drain(ChannelDataStream<H264NalUnit> sub)
  {
    var received = new List<ulong>();
    var cts = new CancellationTokenSource(100);
    await foreach (var item in sub.ReadAsync(cts.Token))
      received.Add(item.Timestamp);
    return received;
  }

  /// <summary>
  /// SCENARIO:
  /// A demanding subscriber has consumed a GOP, then unsubscribes while no other demand remains
  ///
  /// ACTION:
  /// Subscribe again before the next sync point arrives
  ///
  /// EXPECTED RESULT:
  /// The new subscriber does not receive the GOP the previous subscriber already consumed
  /// </summary>
  [Test]
  public async Task LastDemandUnsubscribe_ClearsGopCache()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var first = fanOut.Subscribe();
    fanOut.Write(MakeUnit(1, sync: true));
    fanOut.Write(MakeUnit(2));
    first.Dispose();

    using var second = fanOut.Subscribe();

    Assert.That(await Drain(second), Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A demanding subscriber is still attached after a GOP has been written
  ///
  /// ACTION:
  /// A second subscriber joins mid-GOP
  ///
  /// EXPECTED RESULT:
  /// The second subscriber receives the cached GOP from its sync point
  /// </summary>
  [Test]
  public async Task SubscribeWithLiveDemand_ReplaysGopCache()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    using var first = fanOut.Subscribe();
    fanOut.Write(MakeUnit(1, sync: true));
    fanOut.Write(MakeUnit(2));

    using var second = fanOut.Subscribe();

    Assert.That(await Drain(second), Is.EqualTo(new ulong[] { 1, 2 }));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera resends its parameter sets in-band before every keyframe, for many GOPs,
  /// while a demanding subscriber stays attached
  ///
  /// ACTION:
  /// A second subscriber joins mid-GOP with enough capacity to hold everything cached
  ///
  /// EXPECTED RESULT:
  /// The second subscriber receives only the latest parameter sets and the current GOP
  /// </summary>
  [Test]
  public async Task RepeatedInBandHeaders_DoNotAccumulateInGopCache()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);
    using var first = fanOut.Subscribe();

    const ulong gops = 1000;
    for (ulong gop = 0; gop < gops; gop++)
    {
      var ts = gop * 10;
      fanOut.Write(MakeHeader(ts + 1, H264NalType.Sps));
      fanOut.Write(MakeHeader(ts + 2, H264NalType.Pps));
      fanOut.Write(MakeUnit(ts + 3, sync: true));
      fanOut.Write(MakeUnit(ts + 4));
    }

    using var second = fanOut.Subscribe(capacity: 10_000);

    var lastGop = (gops - 1) * 10;
    Assert.That(await Drain(second),
      Is.EqualTo(new[] { lastGop + 1, lastGop + 2, lastGop + 3, lastGop + 4 }));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera sends its parameter sets once at stream start and never again
  ///
  /// ACTION:
  /// A second subscriber joins after several later GOPs
  ///
  /// EXPECTED RESULT:
  /// The second subscriber still receives the original parameter sets ahead of the current GOP
  /// </summary>
  [Test]
  public async Task HeadersSentOnce_SurviveLaterSyncPoints()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);
    using var first = fanOut.Subscribe();

    fanOut.Write(MakeHeader(1, H264NalType.Sps));
    fanOut.Write(MakeHeader(2, H264NalType.Pps));
    fanOut.Write(MakeUnit(3, sync: true));
    fanOut.Write(MakeUnit(4));
    fanOut.Write(MakeUnit(5, sync: true));
    fanOut.Write(MakeUnit(6));
    fanOut.Write(MakeUnit(7, sync: true));
    fanOut.Write(MakeUnit(8));

    using var second = fanOut.Subscribe();

    Assert.That(await Drain(second), Is.EqualTo(new ulong[] { 1, 2, 7, 8 }));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera changes its parameter sets mid-stream, sending new ones before a keyframe
  ///
  /// ACTION:
  /// A second subscriber joins after the change
  ///
  /// EXPECTED RESULT:
  /// The second subscriber receives the new parameter sets, not the superseded ones
  /// </summary>
  [Test]
  public async Task NewHeaders_ReplaceRetainedHeaders()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);
    using var first = fanOut.Subscribe();

    fanOut.Write(MakeHeader(1, H264NalType.Sps));
    fanOut.Write(MakeHeader(2, H264NalType.Pps));
    fanOut.Write(MakeUnit(3, sync: true));
    fanOut.Write(MakeUnit(4, sync: true));
    fanOut.Write(MakeHeader(5, H264NalType.Sps));
    fanOut.Write(MakeHeader(6, H264NalType.Pps));
    fanOut.Write(MakeUnit(7, sync: true));

    using var second = fanOut.Subscribe();

    Assert.That(await Drain(second), Is.EqualTo(new ulong[] { 5, 6, 7 }));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera in a long-GOP mode sends far more units between keyframes than a subscriber's
  /// channel can hold
  ///
  /// ACTION:
  /// Subscribe mid-GOP, once with the default capacity and once with a very large one
  ///
  /// EXPECTED RESULT:
  /// Neither subscriber is handed a replay with its keyframe missing: both receive only the
  /// parameter sets, because the cache stopped holding the oversized GOP
  /// </summary>
  [Test]
  public async Task OversizedGop_LateSubscribersReceiveOnlyHeaders()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);
    using var first = fanOut.Subscribe();

    fanOut.Write(MakeHeader(1, H264NalType.Sps));
    fanOut.Write(MakeHeader(2, H264NalType.Pps));
    fanOut.Write(MakeUnit(3, sync: true));
    for (ulong ts = 4; ts < 4 + 2 * (ulong)DataStreamFanOut<H264NalUnit>.MaxCachedUnits; ts++)
      fanOut.Write(MakeUnit(ts));

    using var defaultCapacity = fanOut.Subscribe();
    using var largeCapacity = fanOut.Subscribe(capacity: 10_000);

    Assert.That(await Drain(defaultCapacity), Is.EqualTo(new ulong[] { 1, 2 }));
    Assert.That(await Drain(largeCapacity), Is.EqualTo(new ulong[] { 1, 2 }));
  }

  /// <summary>
  /// SCENARIO:
  /// An oversized GOP made the cache fall back to parameter sets only
  ///
  /// ACTION:
  /// The next keyframe arrives with a normal-sized GOP, then a subscriber joins
  ///
  /// EXPECTED RESULT:
  /// Caching resumes at the keyframe and the subscriber receives the whole current GOP
  /// </summary>
  [Test]
  public async Task OversizedGop_CachingResumesAtNextKeyframe()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);
    using var first = fanOut.Subscribe();

    fanOut.Write(MakeUnit(1, sync: true));
    for (ulong ts = 2; ts < 2 + 2 * (ulong)DataStreamFanOut<H264NalUnit>.MaxCachedUnits; ts++)
      fanOut.Write(MakeUnit(ts));

    fanOut.Write(MakeHeader(9001, H264NalType.Sps));
    fanOut.Write(MakeHeader(9002, H264NalType.Pps));
    fanOut.Write(MakeUnit(9003, sync: true));
    fanOut.Write(MakeUnit(9004));

    using var second = fanOut.Subscribe();

    Assert.That(await Drain(second), Is.EqualTo(new ulong[] { 9001, 9002, 9003, 9004 }));
  }

  /// <summary>
  /// SCENARIO:
  /// A single subscriber is connected to a push-based fan-out
  ///
  /// ACTION:
  /// Write items to the fan-out
  ///
  /// EXPECTED RESULT:
  /// Subscriber receives all items
  /// </summary>
  [Test]
  public async Task SingleSubscriber_ReceivesAllItems()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    using var sub = fanOut.Subscribe();

    fanOut.Write(MakeUnit(1));
    fanOut.Write(MakeUnit(2));
    fanOut.Write(MakeUnit(3));

    var received = new List<ulong>();
    var cts = new CancellationTokenSource(100);
    await foreach (var item in sub.ReadAsync(cts.Token))
      received.Add(item.Timestamp);

    Assert.That(received, Is.EqualTo(new ulong[] { 1, 2, 3 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Two independent subscribers connected to the same fan-out
  ///
  /// ACTION:
  /// Write items to the fan-out
  ///
  /// EXPECTED RESULT:
  /// Both subscribers receive all items independently
  /// </summary>
  [Test]
  public async Task MultipleSubscribers_EachReceivesAllItems()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    using var sub1 = fanOut.Subscribe();
    using var sub2 = fanOut.Subscribe();

    fanOut.Write(MakeUnit(10));
    fanOut.Write(MakeUnit(20));

    var cts = new CancellationTokenSource(100);

    var received1 = new List<ulong>();
    await foreach (var item in sub1.ReadAsync(cts.Token))
      received1.Add(item.Timestamp);

    var received2 = new List<ulong>();
    cts = new CancellationTokenSource(100);
    await foreach (var item in sub2.ReadAsync(cts.Token))
      received2.Add(item.Timestamp);

    Assert.That(received1, Is.EqualTo(new ulong[] { 10, 20 }));
    Assert.That(received2, Is.EqualTo(new ulong[] { 10, 20 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Fan-out subscriber has a small capacity (2) and 5 items are written rapidly
  ///
  /// ACTION:
  /// Write 5 items without reading, then read
  ///
  /// EXPECTED RESULT:
  /// Oldest items are dropped, subscriber receives the most recent items
  /// </summary>
  [Test]
  public async Task BackpressureDropsOldest()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    using var sub = fanOut.Subscribe(capacity: 2);

    for (ulong i = 1; i <= 5; i++)
      fanOut.Write(MakeUnit(i));

    var received = new List<ulong>();
    var cts = new CancellationTokenSource(100);
    await foreach (var item in sub.ReadAsync(cts.Token))
      received.Add(item.Timestamp);

    Assert.That(received.Count, Is.LessThanOrEqualTo(2));
    Assert.That(received[^1], Is.EqualTo(5));
  }

  /// <summary>
  /// SCENARIO:
  /// Two subscribers are connected, then both are disposed
  ///
  /// ACTION:
  /// Check SubscriberCount at each stage
  ///
  /// EXPECTED RESULT:
  /// Count reflects active subscribers: 0 -> 1 -> 2 -> 1 -> 0
  /// </summary>
  [Test]
  public async Task SubscriberCount_TracksActiveSubscribers()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    Assert.That(fanOut.SubscriberCount, Is.EqualTo(0));

    var sub1 = fanOut.Subscribe();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));

    var sub2 = fanOut.Subscribe();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(2));

    sub1.Dispose();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));

    sub2.Dispose();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// Demand subscribers are added and removed
  ///
  /// ACTION:
  /// Check GetDemand at each stage
  ///
  /// EXPECTED RESULT:
  /// GetDemand reflects the live demand subscribers: 0 -> 1 -> 2 -> 1 -> 0
  /// </summary>
  [Test]
  public async Task GetDemand_TracksLiveDemandSubscribers()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    Assert.That(fanOut.GetDemand(), Is.EqualTo(0));

    var sub1 = fanOut.Subscribe();
    Assert.That(fanOut.GetDemand(), Is.EqualTo(1));

    var sub2 = fanOut.Subscribe();
    Assert.That(fanOut.GetDemand(), Is.EqualTo(2));

    sub1.Dispose();
    Assert.That(fanOut.GetDemand(), Is.EqualTo(1));

    sub2.Dispose();
    Assert.That(fanOut.GetDemand(), Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// A passive subscriber is connected to the fan-out
  ///
  /// ACTION:
  /// Write items, read them, check GetDemand
  ///
  /// EXPECTED RESULT:
  /// Passive subscriber receives items but contributes no demand
  /// </summary>
  [Test]
  public async Task PassiveSubscriber_ContributesNoDemand()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    using var sub = fanOut.SubscribePassive();

    Assert.That(fanOut.GetDemand(), Is.EqualTo(0));
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));

    fanOut.Write(MakeUnit(1));

    var received = new List<ulong>();
    var cts = new CancellationTokenSource(100);
    await foreach (var item in sub.ReadAsync(cts.Token))
      received.Add(item.Timestamp);

    Assert.That(received, Is.EqualTo(new ulong[] { 1 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Changed callback is set on the fan-out
  ///
  /// ACTION:
  /// Subscribe (demand and passive), then dispose both
  ///
  /// EXPECTED RESULT:
  /// Changed fires on every subscribe and unsubscribe
  /// </summary>
  [Test]
  public async Task Changed_FiresOnEverySubscriptionChange()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var changedCount = 0;
    fanOut.Changed = () => changedCount++;

    var sub1 = fanOut.Subscribe();
    Assert.That(changedCount, Is.EqualTo(1));

    var sub2 = fanOut.SubscribePassive();
    Assert.That(changedCount, Is.EqualTo(2));

    sub1.Dispose();
    Assert.That(changedCount, Is.EqualTo(3));

    sub2.Dispose();
    Assert.That(changedCount, Is.EqualTo(4));
  }

  /// <summary>
  /// SCENARIO:
  /// ChannelDataStream is disposed twice
  ///
  /// ACTION:
  /// Call Dispose twice
  ///
  /// EXPECTED RESULT:
  /// Second dispose is a no-op, subscriber count doesn't go negative
  /// </summary>
  [Test]
  public async Task ChannelDataStream_DoubleDispose_IsIdempotent()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var sub = fanOut.Subscribe();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));

    sub.Dispose();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(0));

    sub.Dispose();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// ReadAsync is called on the fan-out directly (not via subscriber)
  ///
  /// ACTION:
  /// Write items, read via ReadAsync
  ///
  /// EXPECTED RESULT:
  /// Creates an internal subscriber and returns items
  /// </summary>
  [Test]
  public async Task ReadAsync_CreatesInternalSubscriber()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var readTask = Task.Run(async () =>
    {
      var received = new List<ulong>();
      var cts = new CancellationTokenSource(200);
      await foreach (var item in fanOut.ReadAsync(cts.Token))
        received.Add(item.Timestamp);
      return received;
    });

    await Task.Delay(50);
    fanOut.Write(MakeUnit(1));
    fanOut.Write(MakeUnit(2));

    var received = await readTask;
    Assert.That(received, Is.EqualTo(new ulong[] { 1, 2 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Fan-out Info property returns the StreamInfo passed at construction
  ///
  /// ACTION:
  /// Read Info
  ///
  /// EXPECTED RESULT:
  /// Returns the same StreamInfo
  /// </summary>
  [Test]
  public void Info_ReturnsConstructionInfo()
  {
    var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    Assert.That(fanOut.Info.DataFormat, Is.EqualTo("h264"));
  }
}
