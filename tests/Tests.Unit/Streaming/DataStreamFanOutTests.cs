using System.Threading.Channels;
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
  /// A single subscriber is connected to a fan-out
  ///
  /// ACTION:
  /// Write items to the source stream
  ///
  /// EXPECTED RESULT:
  /// Subscriber receives all items
  /// </summary>
  [Test]
  public async Task SingleSubscriber_ReceivesAllItems()
  {
    var source = new TestDataStream<H264NalUnit>(TestInfo);
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(source);

    var sub = fanOut.Subscribe();
    fanOut.Start();

    source.Write(MakeUnit(1));
    source.Write(MakeUnit(2));
    source.Write(MakeUnit(3));
    source.Complete();

    var received = new List<ulong>();
    await foreach (var item in ((IDataStream<H264NalUnit>)sub).ReadAsync(CancellationToken.None))
      received.Add(item.Timestamp);

    Assert.That(received, Is.EqualTo(new ulong[] { 1, 2, 3 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Two independent subscribers connected to the same fan-out
  ///
  /// ACTION:
  /// Write items to the source
  ///
  /// EXPECTED RESULT:
  /// Both subscribers receive all items independently
  /// </summary>
  [Test]
  public async Task MultipleSubscribers_EachReceivesAllItems()
  {
    var source = new TestDataStream<H264NalUnit>(TestInfo);
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(source);

    var sub1 = fanOut.Subscribe();
    var sub2 = fanOut.Subscribe();
    fanOut.Start();

    source.Write(MakeUnit(10));
    source.Write(MakeUnit(20));
    source.Complete();

    var received1 = new List<ulong>();
    await foreach (var item in ((IDataStream<H264NalUnit>)sub1).ReadAsync(CancellationToken.None))
      received1.Add(item.Timestamp);

    var received2 = new List<ulong>();
    await foreach (var item in ((IDataStream<H264NalUnit>)sub2).ReadAsync(CancellationToken.None))
      received2.Add(item.Timestamp);

    Assert.That(received1, Is.EqualTo(new ulong[] { 10, 20 }));
    Assert.That(received2, Is.EqualTo(new ulong[] { 10, 20 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Fan-out has a small capacity (2) and source writes 5 items rapidly
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
    var source = new TestDataStream<H264NalUnit>(TestInfo);
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(source);

    var sub = fanOut.Subscribe(capacity: 2);
    fanOut.Start();

    for (ulong i = 1; i <= 5; i++)
      source.Write(MakeUnit(i));

    // give the fan-out loop time to distribute
    await Task.Delay(50);
    source.Complete();

    var received = new List<ulong>();
    await foreach (var item in ((IDataStream<H264NalUnit>)sub).ReadAsync(CancellationToken.None))
      received.Add(item.Timestamp);

    Assert.That(received.Count, Is.LessThanOrEqualTo(2));
    Assert.That(received[^1], Is.EqualTo(5));
  }

  /// <summary>
  /// SCENARIO:
  /// Two subscribers are connected, then both finish reading
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
    var source = new TestDataStream<H264NalUnit>(TestInfo);
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(source);

    Assert.That(fanOut.SubscriberCount, Is.EqualTo(0));

    var sub1 = fanOut.Subscribe();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));

    var sub2 = fanOut.Subscribe();
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(2));

    fanOut.Start();
    source.Complete();

    await foreach (var _ in ((IDataStream<H264NalUnit>)sub1).ReadAsync(CancellationToken.None)) { }
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));

    await foreach (var _ in ((IDataStream<H264NalUnit>)sub2).ReadAsync(CancellationToken.None)) { }
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// OnEmpty callback is set on a fan-out with one subscriber
  ///
  /// ACTION:
  /// Subscriber finishes reading (last subscriber leaves)
  ///
  /// EXPECTED RESULT:
  /// OnEmpty callback is invoked
  /// </summary>
  [Test]
  public async Task OnEmpty_InvokedWhenLastSubscriberLeaves()
  {
    var source = new TestDataStream<H264NalUnit>(TestInfo);
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(source);

    var emptyCalled = false;
    fanOut.OnEmpty = () => emptyCalled = true;

    var sub = fanOut.Subscribe();
    fanOut.Start();
    source.Complete();

    await foreach (var _ in ((IDataStream<H264NalUnit>)sub).ReadAsync(CancellationToken.None)) { }

    Assert.That(emptyCalled, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// OnEmpty callback is set on a fan-out with two subscribers
  ///
  /// ACTION:
  /// First subscriber finishes reading
  ///
  /// EXPECTED RESULT:
  /// OnEmpty is NOT invoked (one subscriber remains)
  /// </summary>
  [Test]
  public async Task OnEmpty_NotInvokedWhileSubscribersRemain()
  {
    var source = new TestDataStream<H264NalUnit>(TestInfo);
    await using var fanOut = new DataStreamFanOut<H264NalUnit>(source);

    var emptyCalled = false;
    fanOut.OnEmpty = () => emptyCalled = true;

    var sub1 = fanOut.Subscribe();
    var sub2 = fanOut.Subscribe();
    fanOut.Start();
    source.Complete();

    await foreach (var _ in ((IDataStream<H264NalUnit>)sub1).ReadAsync(CancellationToken.None)) { }

    Assert.That(emptyCalled, Is.False);
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));

    await foreach (var _ in ((IDataStream<H264NalUnit>)sub2).ReadAsync(CancellationToken.None)) { }

    Assert.That(emptyCalled, Is.True);
  }

  private sealed class TestDataStream<T> : IDataStream<T> where T : IDataUnit
  {
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

    public StreamInfo Info { get; }
    public Type FrameType => typeof(T);

    public TestDataStream(StreamInfo info) => Info = info;

    public void Write(T item) => _channel.Writer.TryWrite(item);
    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<T> ReadAsync(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
      await foreach (var item in _channel.Reader.ReadAllAsync(ct))
        yield return item;
    }
  }
}
