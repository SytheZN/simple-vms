using Capture.Rtsp;
using Shared.Models;
using Shared.Models.Formats;

namespace Tests.Unit.Streaming;

[TestFixture]
public class DataStreamTests
{
  private static H264NalUnit MakeUnit(ulong ts) => new()
  {
    Data = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65 },
    Timestamp = ts,
    IsSyncPoint = false,
    NalType = H264NalType.Slice
  };

  /// <summary>
  /// SCENARIO:
  /// DataStream created with default capacity
  ///
  /// ACTION:
  /// Check Info and FrameType
  ///
  /// EXPECTED RESULT:
  /// Info matches constructor arg, FrameType is H264NalUnit
  /// </summary>
  [Test]
  public void Properties_MatchConstructorArgs()
  {
    var info = new StreamInfo { DataFormat = "h264" };
    var stream = new DataStream<H264NalUnit>(info);

    Assert.That(stream.Info, Is.SameAs(info));
    Assert.That(stream.FrameType, Is.EqualTo(typeof(H264NalUnit)));
  }

  /// <summary>
  /// SCENARIO:
  /// Items written to DataStream
  ///
  /// ACTION:
  /// Write items, complete, read all
  ///
  /// EXPECTED RESULT:
  /// Reader receives all items in order
  /// </summary>
  [Test]
  public async Task WriteAndRead_ReceivesAllItems()
  {
    var stream = new DataStream<H264NalUnit>(new StreamInfo { DataFormat = "h264" });

    await stream.Writer.WriteAsync(MakeUnit(1));
    await stream.Writer.WriteAsync(MakeUnit(2));
    await stream.Writer.WriteAsync(MakeUnit(3));
    stream.Complete();

    var timestamps = new List<ulong>();
    await foreach (var item in stream.ReadAsync(CancellationToken.None))
      timestamps.Add(item.Timestamp);

    Assert.That(timestamps, Is.EqualTo(new ulong[] { 1, 2, 3 }));
  }

  /// <summary>
  /// SCENARIO:
  /// DataStream completed without writing
  ///
  /// ACTION:
  /// Complete then read
  ///
  /// EXPECTED RESULT:
  /// ReadAsync yields nothing
  /// </summary>
  [Test]
  public async Task Complete_WithoutWrites_ReaderEndsImmediately()
  {
    var stream = new DataStream<H264NalUnit>(new StreamInfo { DataFormat = "h264" });
    stream.Complete();

    var count = 0;
    await foreach (var _ in stream.ReadAsync(CancellationToken.None))
      count++;

    Assert.That(count, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// DataStream with capacity 2, 5 items written rapidly
  ///
  /// ACTION:
  /// Write 5 items then complete and read
  ///
  /// EXPECTED RESULT:
  /// Oldest items dropped, most recent retained
  /// </summary>
  [Test]
  public async Task BoundedCapacity_DropsOldest()
  {
    var stream = new DataStream<H264NalUnit>(new StreamInfo { DataFormat = "h264" }, capacity: 2);

    for (ulong i = 1; i <= 5; i++)
      stream.Writer.TryWrite(MakeUnit(i));

    stream.Complete();

    var timestamps = new List<ulong>();
    await foreach (var item in stream.ReadAsync(CancellationToken.None))
      timestamps.Add(item.Timestamp);

    Assert.That(timestamps, Has.Count.LessThanOrEqualTo(2));
    Assert.That(timestamps[^1], Is.EqualTo(5));
  }

  /// <summary>
  /// SCENARIO:
  /// ReadAsync is cancelled via token
  ///
  /// ACTION:
  /// Start reading, cancel the token
  ///
  /// EXPECTED RESULT:
  /// OperationCanceledException thrown
  /// </summary>
  [Test]
  public void ReadAsync_Cancellation_Throws()
  {
    var stream = new DataStream<H264NalUnit>(new StreamInfo { DataFormat = "h264" });
    var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.ThrowsAsync<TaskCanceledException>(async () =>
    {
      await foreach (var _ in stream.ReadAsync(cts.Token)) { }
    });
  }
}
