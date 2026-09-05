using Microsoft.Extensions.Logging.Abstractions;
using Server.Tunnel;
using Shared.Protocol;

namespace Tests.Unit.Tunnel;

[TestFixture]
public class TunnelStreamSinkTests
{
  /// <summary>
  /// SCENARIO:
  /// A stream channel replaces its running operation (e.g. a fetch command
  /// cancels the initial playback op); the cancelled op's send observes its
  /// cancelled token
  ///
  /// ACTION:
  /// Send with a cancelled token, then send with a live token
  ///
  /// EXPECTED RESULT:
  /// The cancellation propagates to the cancelled op; the sink stays open and
  /// the follow-up send reaches the transport
  /// </summary>
  [Test]
  public async Task Send_CancelledOpToken_SinkStaysUsableForNextOp()
  {
    var transport = new MemoryStream();
    var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);
    var sink = new TunnelStreamSink(muxer, 1);
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();

    Assert.CatchAsync<OperationCanceledException>(
      () => sink.SendStatusAsync(StreamStatus.Ack, cancelled.Token));
    Assert.That(sink.IsOpen, Is.True);

    await sink.SendStatusAsync(StreamStatus.Ack, CancellationToken.None);

    Assert.That(sink.IsOpen, Is.True);
    Assert.That(transport.Length, Is.GreaterThan(0));
  }

  /// <summary>
  /// SCENARIO:
  /// Close is called when the channel handler exits
  ///
  /// ACTION:
  /// Close, then attempt a send
  ///
  /// EXPECTED RESULT:
  /// The sink reports closed and nothing reaches the transport
  /// </summary>
  [Test]
  public async Task Send_AfterClose_IsDropped()
  {
    var transport = new MemoryStream();
    var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);
    var sink = new TunnelStreamSink(muxer, 1);

    sink.Close();
    await sink.SendStatusAsync(StreamStatus.Ack, CancellationToken.None);

    Assert.That(sink.IsOpen, Is.False);
    Assert.That(transport.Length, Is.Zero);
  }
}
