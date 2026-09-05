using System.Buffers.Binary;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Streaming;
using Server.Tunnel.Handlers;
using Shared.Protocol;
using Tests.Unit.Mocks;
using Tests.Unit.Streaming;

namespace Tests.Unit.Tunnel;

[TestFixture]
public class StreamCommandLoopTests
{
  /// <summary>
  /// SCENARIO:
  /// A fetch command arrives on a stream channel while the initial operation
  /// (e.g. a live session) is still running
  ///
  /// ACTION:
  /// Run the loop with a never-ending initial op, write a fetch command,
  /// then close the channel
  ///
  /// EXPECTED RESULT:
  /// The initial op is cancelled and the fetch runs on the same channel
  /// (observable via its Ack + Recording statuses on the sink)
  /// </summary>
  [Test]
  public async Task FetchCommand_CancelsInitialOpAndRunsFetch()
  {
    var channel = Channel.CreateUnbounded<MuxMessage>();
    var sink = new TestStreamSink();
    var initialCancelled = new TaskCompletionSource();

    var loop = StreamCommandLoop.RunAsync(
      Guid.NewGuid(),
      async ct =>
      {
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { initialCancelled.TrySetResult(); throw; }
      },
      channel.Reader, sink, new StreamTapRegistry(),
      new FakePluginHost { DataProvider = new StubDataProvider() },
      NullLogger.Instance, CancellationToken.None);

    channel.Writer.TryWrite(new MuxMessage(0, FetchCommand("motion-grid-main", 1000, 2000)));
    await initialCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    channel.Writer.TryComplete();
    await loop.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.That(sink.Statuses, Does.Contain(StreamStatus.Ack));
    Assert.That(sink.Statuses, Does.Contain(StreamStatus.Recording));
  }

  /// <summary>
  /// SCENARIO:
  /// The client closes the stream channel while an operation is running
  ///
  /// ACTION:
  /// Run the loop with a never-ending initial op and complete the channel
  ///
  /// EXPECTED RESULT:
  /// The loop returns and the running operation is cancelled
  /// </summary>
  [Test]
  public async Task ChannelClosed_CancelsRunningOp()
  {
    var channel = Channel.CreateUnbounded<MuxMessage>();
    var sink = new TestStreamSink();
    var initialCancelled = new TaskCompletionSource();

    var loop = StreamCommandLoop.RunAsync(
      Guid.NewGuid(),
      async ct =>
      {
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { initialCancelled.TrySetResult(); throw; }
      },
      channel.Reader, sink, new StreamTapRegistry(),
      new FakePluginHost { DataProvider = new StubDataProvider() },
      NullLogger.Instance, CancellationToken.None);

    channel.Writer.TryComplete();

    await loop.WaitAsync(TimeSpan.FromSeconds(5));
    await initialCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
  }

  /// <summary>
  /// SCENARIO:
  /// A live command arrives on a stream channel (e.g. a playback channel
  /// returning to live)
  ///
  /// ACTION:
  /// Run the loop, write a live command for a camera with no pipeline,
  /// then close the channel
  ///
  /// EXPECTED RESULT:
  /// The live session runs and reports its statuses on the same channel
  /// (Ack + Live, then Error because no pipeline exists)
  /// </summary>
  [Test]
  public async Task LiveCommand_RunsLiveSession()
  {
    var channel = Channel.CreateUnbounded<MuxMessage>();
    var sink = new TestStreamSink();

    var loop = StreamCommandLoop.RunAsync(
      Guid.NewGuid(),
      _ => Task.CompletedTask,
      channel.Reader, sink, new StreamTapRegistry(),
      new FakePluginHost { DataProvider = new StubDataProvider() },
      NullLogger.Instance, CancellationToken.None);

    channel.Writer.TryWrite(new MuxMessage(0, LiveCommand("main")));
    channel.Writer.TryComplete();
    await loop.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.That(sink.Statuses, Does.Contain(StreamStatus.Live));
    Assert.That(sink.Statuses, Does.Contain(StreamStatus.Error));
  }

  private static byte[] FetchCommand(string profile, ulong from, ulong to)
  {
    var profileBytes = Encoding.UTF8.GetBytes(profile);
    var payload = new byte[2 + profileBytes.Length + 16];
    payload[0] = (byte)ClientMessageType.Fetch;
    payload[1] = (byte)profileBytes.Length;
    profileBytes.CopyTo(payload, 2);
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(2 + profileBytes.Length), from);
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(2 + profileBytes.Length + 8), to);
    return payload;
  }

  private static byte[] LiveCommand(string profile)
  {
    var profileBytes = Encoding.UTF8.GetBytes(profile);
    var payload = new byte[2 + profileBytes.Length];
    payload[0] = (byte)ClientMessageType.Live;
    payload[1] = (byte)profileBytes.Length;
    profileBytes.CopyTo(payload, 2);
    return payload;
  }
}
