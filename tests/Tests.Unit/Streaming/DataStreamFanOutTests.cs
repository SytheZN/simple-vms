using Server.Streaming;
using Shared.Models;
using Shared.Models.Formats;

namespace Tests.Unit.Streaming;

[TestFixture]
public class DataStreamFanOutTests
{
  private static StreamInfo TestInfo => new() { DataFormat = "h264" };

  private static H264NalUnit MakeUnit(ulong ts) => new()
  {
    Data = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65 },
    Timestamp = ts,
    IsSyncPoint = false,
    NalType = H264NalType.Slice
  };

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
    try
    {
      await foreach (var item in sub.ReadAsync(cts.Token))
        received.Add(item.Timestamp);
    }
    catch (OperationCanceledException) { }

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
    try
    {
      await foreach (var item in sub1.ReadAsync(cts.Token))
        received1.Add(item.Timestamp);
    }
    catch (OperationCanceledException) { }

    var received2 = new List<ulong>();
    cts = new CancellationTokenSource(100);
    try
    {
      await foreach (var item in sub2.ReadAsync(cts.Token))
        received2.Add(item.Timestamp);
    }
    catch (OperationCanceledException) { }

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
    try
    {
      await foreach (var item in sub.ReadAsync(cts.Token))
        received.Add(item.Timestamp);
    }
    catch (OperationCanceledException) { }

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
  /// OnEmpty callback is set on a fan-out with one demand subscriber
  ///
  /// ACTION:
  /// Subscriber is disposed (last demand subscriber leaves)
  ///
  /// EXPECTED RESULT:
  /// OnEmpty callback is invoked
  /// </summary>
  [Test]
  public async Task OnEmpty_InvokedWhenLastSubscriberLeaves()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var emptyCalled = false;
    fanOut.OnEmpty = () => emptyCalled = true;

    var sub = fanOut.Subscribe();
    sub.Dispose();

    Assert.That(emptyCalled, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// OnEmpty callback is set on a fan-out with two demand subscribers
  ///
  /// ACTION:
  /// First subscriber is disposed
  ///
  /// EXPECTED RESULT:
  /// OnEmpty is NOT invoked (one subscriber remains)
  /// </summary>
  [Test]
  public async Task OnEmpty_NotInvokedWhileSubscribersRemain()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var emptyCalled = false;
    fanOut.OnEmpty = () => emptyCalled = true;

    var sub1 = fanOut.Subscribe();
    var sub2 = fanOut.Subscribe();

    sub1.Dispose();
    Assert.That(emptyCalled, Is.False);
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));

    sub2.Dispose();
    Assert.That(emptyCalled, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A passive subscriber is connected to the fan-out
  ///
  /// ACTION:
  /// Write items, then dispose the passive subscriber
  ///
  /// EXPECTED RESULT:
  /// Passive subscriber receives items but does not trigger OnEmpty or OnDemand
  /// </summary>
  [Test]
  public async Task PassiveSubscriber_DoesNotTriggerDemand()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var demandCalled = false;
    var emptyCalled = false;
    fanOut.OnDemand = () => demandCalled = true;
    fanOut.OnEmpty = () => emptyCalled = true;

    using var sub = fanOut.SubscribePassive();

    Assert.That(demandCalled, Is.False);

    fanOut.Write(MakeUnit(1));

    var received = new List<ulong>();
    var cts = new CancellationTokenSource(100);
    try
    {
      await foreach (var item in sub.ReadAsync(cts.Token))
        received.Add(item.Timestamp);
    }
    catch (OperationCanceledException) { }

    Assert.That(received, Is.EqualTo(new ulong[] { 1 }));

    sub.Dispose();
    Assert.That(emptyCalled, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// OnDemand callback is set, first demand subscriber is added
  ///
  /// ACTION:
  /// Subscribe (demand)
  ///
  /// EXPECTED RESULT:
  /// OnDemand fires
  /// </summary>
  [Test]
  public async Task OnDemand_FiredOnFirstDemandSubscriber()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var demandCalled = false;
    fanOut.OnDemand = () => demandCalled = true;

    fanOut.Subscribe();

    Assert.That(demandCalled, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Two demand subscribers added
  ///
  /// ACTION:
  /// Subscribe twice
  ///
  /// EXPECTED RESULT:
  /// OnDemand fires only once (on first subscriber)
  /// </summary>
  [Test]
  public async Task OnDemand_FiredOnlyOnce()
  {
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(TestInfo);

    var demandCount = 0;
    fanOut.OnDemand = () => demandCount++;

    fanOut.Subscribe();
    fanOut.Subscribe();

    Assert.That(demandCount, Is.EqualTo(1));
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
      try
      {
        await foreach (var item in fanOut.ReadAsync(cts.Token))
          received.Add(item.Timestamp);
      }
      catch (OperationCanceledException) { }
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
