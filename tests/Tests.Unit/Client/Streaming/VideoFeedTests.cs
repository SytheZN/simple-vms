using System.Threading.Channels;
using Client.Core.Streaming;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;

namespace Tests.Unit.Client.Streaming;

[TestFixture]
public class VideoFeedTests
{
  /// <summary>
  /// SCENARIO:
  /// The server sends a serialized Status frame carrying StreamStatus.Ack
  /// (the subscription acknowledgement that clears the client's ignoreData gate)
  ///
  /// ACTION:
  /// Construct a VideoFeed over an in-memory MuxStream, subscribe to OnStatus,
  /// start the read loop, and write an Ack frame into the channel
  ///
  /// EXPECTED RESULT:
  /// OnStatus fires exactly once with StreamStatus.Ack
  /// </summary>
  [Test]
  public async Task ReadLoop_ParsesAck_FiresOnStatus()
  {
    await using var rig = new FeedRig();

    StreamStatus? received = null;
    var signalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Feed.OnStatus += s => { received = s; signalled.TrySetResult(true); };

    rig.Start();
    await rig.WriteAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Ack));
    await AwaitOrFailAsync(signalled.Task, "OnStatus");

    Assert.That(received, Is.EqualTo(StreamStatus.Ack));
  }

  /// <summary>
  /// SCENARIO:
  /// The server sends an Init frame describing the stream's codec init segment
  ///
  /// ACTION:
  /// Write a SerializeInit payload, await OnInit
  ///
  /// EXPECTED RESULT:
  /// OnInit fires once with the init bytes; LastInit reflects the same bytes
  /// </summary>
  [Test]
  public async Task ReadLoop_ParsesInit_FiresOnInitAndStoresLastInit()
  {
    await using var rig = new FeedRig();
    var initBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

    var received = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Feed.OnInit += d => received.TrySetResult(d);

    rig.Start();
    await rig.WriteAsync(StreamMessageWriter.SerializeInit("main", initBytes));
    var got = await AwaitOrFailAsync(received.Task, "OnInit");

    Assert.Multiple(() =>
    {
      Assert.That(got.ToArray(), Is.EqualTo(initBytes));
      Assert.That(rig.Feed.LastInit.ToArray(), Is.EqualTo(initBytes));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The server sends a Gop fragment with Begin|End flags
  ///
  /// ACTION:
  /// Write a SerializeGop payload, await OnGop
  ///
  /// EXPECTED RESULT:
  /// OnGop fires once with matching flags, profile, timestamp and payload
  /// </summary>
  [Test]
  public async Task ReadLoop_ParsesGop_FiresOnGop()
  {
    await using var rig = new FeedRig();
    var data = new byte[] { 0x01, 0x02, 0x03 };

    var received = new TaskCompletionSource<GopMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Feed.OnGop += g => received.TrySetResult(g);

    rig.Start();
    await rig.WriteAsync(StreamMessageWriter.SerializeGop(
      GopFlags.Begin | GopFlags.End, "main", 12345, data));
    var got = await AwaitOrFailAsync(received.Task, "OnGop");

    Assert.Multiple(() =>
    {
      Assert.That(got.Flags, Is.EqualTo(GopFlags.Begin | GopFlags.End));
      Assert.That(got.Profile, Is.EqualTo("main"));
      Assert.That(got.Timestamp, Is.EqualTo(12345UL));
      Assert.That(got.Data.ToArray(), Is.EqualTo(data));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The server sends a Gap status frame (recording hole between two timestamps)
  ///
  /// ACTION:
  /// Write a SerializeGap payload, await OnGap
  ///
  /// EXPECTED RESULT:
  /// OnGap fires once with matching from/to; OnStatus does NOT fire
  /// (the dispatch routes Gap to its own handler)
  /// </summary>
  [Test]
  public async Task ReadLoop_ParsesGap_FiresOnGap_NotOnStatus()
  {
    await using var rig = new FeedRig();

    var gap = new TaskCompletionSource<GapStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
    var statusFired = false;
    rig.Feed.OnGap += g => gap.TrySetResult(g);
    rig.Feed.OnStatus += _ => statusFired = true;

    rig.Start();
    await rig.WriteAsync(StreamMessageWriter.SerializeGap(1000, 2000));
    var got = await AwaitOrFailAsync(gap.Task, "OnGap");

    Assert.Multiple(() =>
    {
      Assert.That(got.From, Is.EqualTo(1000UL));
      Assert.That(got.To, Is.EqualTo(2000UL));
      Assert.That(statusFired, Is.False);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The mux channel completes without any payload (clean close)
  ///
  /// ACTION:
  /// Start the feed, complete the writer, await OnCompleted
  ///
  /// EXPECTED RESULT:
  /// OnCompleted fires exactly once
  /// </summary>
  [Test]
  public async Task ReadLoop_ChannelComplete_FiresOnCompleted()
  {
    await using var rig = new FeedRig();
    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Feed.OnCompleted += () => completed.TrySetResult();

    rig.Start();
    rig.Channel.Writer.Complete();

    await AwaitOrFailAsync(completed.Task, "OnCompleted");
  }

  /// <summary>
  /// SCENARIO:
  /// A message with an empty payload arrives between real messages
  ///
  /// ACTION:
  /// Write empty MuxMessage, then a real Status frame
  ///
  /// EXPECTED RESULT:
  /// Empty payload is skipped silently; Status still parses and fires
  /// </summary>
  [Test]
  public async Task ReadLoop_EmptyPayload_Skipped()
  {
    await using var rig = new FeedRig();
    var statusReceived = new TaskCompletionSource<StreamStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Feed.OnStatus += s => statusReceived.TrySetResult(s);

    rig.Start();
    await rig.Channel.Writer.WriteAsync(new MuxMessage(0, ReadOnlyMemory<byte>.Empty));
    await rig.WriteAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Live));

    var got = await AwaitOrFailAsync(statusReceived.Task, "OnStatus");
    Assert.That(got, Is.EqualTo(StreamStatus.Live));
  }

  /// <summary>
  /// SCENARIO:
  /// SendFetchAsync builds the binary fetch request and writes it onto the mux
  ///
  /// ACTION:
  /// Open a sink mux to capture the framed bytes, then call SendFetchAsync
  ///
  /// EXPECTED RESULT:
  /// One mux frame is written carrying:
  ///   [Fetch type byte][profile len byte][profile bytes][from BE u64][to BE u64]
  /// </summary>
  [Test]
  public async Task SendFetchAsync_WritesEncodedRequestToTransport()
  {
    var sink = new MemoryStream();
    var muxer = new StreamMuxer(sink, NullLogger.Instance, 1);
    var channel = Channel.CreateUnbounded<MuxMessage>();
    var stream = new MuxStream(muxer, streamId: 1, channel.Reader, NullLogger.Instance);
    var feed = new VideoFeed(stream, Guid.NewGuid(), "main", NullLogger.Instance);

    await feed.SendFetchAsync(from: 0x0102030405060708UL, to: 0x1112131415161718UL,
      CancellationToken.None);

    var framed = sink.ToArray();
    Assert.That(framed.Length, Is.GreaterThan(MessageEnvelope.MuxHeaderSize));

    var payload = framed.AsMemory(MessageEnvelope.MuxHeaderSize);
    Assert.Multiple(() =>
    {
      Assert.That(payload.Span[0], Is.EqualTo((byte)ClientMessageType.Fetch));
      Assert.That(payload.Span[1], Is.EqualTo((byte)4));
      Assert.That(System.Text.Encoding.UTF8.GetString(payload.Slice(2, 4).Span),
        Is.EqualTo("main"));
      Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(payload[6..].Span),
        Is.EqualTo(0x0102030405060708UL));
      Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(payload[14..].Span),
        Is.EqualTo(0x1112131415161718UL));
    });
  }

  private static async Task<T> AwaitOrFailAsync<T>(Task<T> task, string what)
  {
    var winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
    Assert.That(winner, Is.SameAs(task), $"{what} did not fire within 2s");
    return await task;
  }

  private static async Task AwaitOrFailAsync(Task task, string what)
  {
    var winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
    Assert.That(winner, Is.SameAs(task), $"{what} did not fire within 2s");
    await task;
  }

  private sealed class FeedRig : IAsyncDisposable
  {
    public Channel<MuxMessage> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<MuxMessage>();
    public StreamMuxer Muxer { get; }
    public MuxStream Stream { get; }
    public VideoFeed Feed { get; }

    public FeedRig()
    {
      Muxer = new StreamMuxer(new MemoryStream(), NullLogger.Instance, 1);
      Stream = new MuxStream(Muxer, 1, Channel.Reader, NullLogger.Instance);
      Feed = new VideoFeed(Stream, Guid.NewGuid(), "main", NullLogger.Instance);
    }

    public void Start() => Feed.Start(CancellationToken.None);
    public ValueTask WriteAsync(byte[] payload) =>
      Channel.Writer.WriteAsync(new MuxMessage(0, payload));

    public async ValueTask DisposeAsync()
    {
      Channel.Writer.TryComplete();
      await Feed.DisposeAsync();
    }
  }
}
