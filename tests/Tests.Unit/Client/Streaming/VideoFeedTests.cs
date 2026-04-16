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
    var channel = Channel.CreateUnbounded<MuxMessage>();
    var muxer = new StreamMuxer(new MemoryStream(), NullLogger.Instance, 1);
    var stream = new MuxStream(muxer, 1, channel.Reader);
    var feed = new VideoFeed(stream, Guid.NewGuid(), "main", NullLogger.Instance);

    StreamStatus? received = null;
    var signalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    feed.OnStatus += s =>
    {
      received = s;
      signalled.TrySetResult(true);
    };

    feed.Start(CancellationToken.None);

    var payload = StreamMessageWriter.SerializeStatus(StreamStatus.Ack);
    await channel.Writer.WriteAsync(new MuxMessage(0, payload));

    var fired = await Task.WhenAny(signalled.Task, Task.Delay(TimeSpan.FromSeconds(2)))
      == signalled.Task;

    Assert.That(fired, Is.True, "OnStatus did not fire within 2s");
    Assert.That(received, Is.EqualTo(StreamStatus.Ack));

    await feed.DisposeAsync();
  }
}
