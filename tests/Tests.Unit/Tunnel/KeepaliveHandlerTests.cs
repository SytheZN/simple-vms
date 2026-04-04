using System.Threading.Channels;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tunnel;
using Server.Tunnel.Handlers;
using Shared.Protocol;

namespace Tests.Unit.Tunnel;

[TestFixture]
public class KeepaliveHandlerTests
{
  /// <summary>
  /// SCENARIO:
  /// A single keepalive message is delivered via channel
  ///
  /// ACTION:
  /// Run KeepaliveHandler, which reads the message and writes an echo reply
  ///
  /// EXPECTED RESULT:
  /// The echo reply contains the same Echo value as the request
  /// </summary>
  [Test]
  public async Task HandleKeepalive_SingleMessage_EchoesValue()
  {
    var msg = new KeepaliveMessage { Echo = 0xDEADBEEF };
    var payload = MessagePackSerializer.Serialize(msg, ProtocolSerializer.Options);

    var inputChannel = Channel.CreateUnbounded<MuxMessage>();
    await inputChannel.Writer.WriteAsync(new MuxMessage(0, payload));
    inputChannel.Writer.Complete();

    KeepaliveMessage? response = null;

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    try
    {
      await KeepaliveHandler.RunAsync(
        inputChannel.Reader,
        (data, ct) =>
        {
          response = MessagePackSerializer.Deserialize<KeepaliveMessage>(
            data, ProtocolSerializer.Options);
          return Task.CompletedTask;
        },
        () => { }, NullLogger.Instance, cts.Token);
    }
    catch (OperationCanceledException) { }

    Assert.That(response, Is.Not.Null);
    Assert.That(response!.Echo, Is.EqualTo(0xDEADBEEFUL));
  }

  /// <summary>
  /// SCENARIO:
  /// The keepalive channel is immediately completed (no messages)
  ///
  /// ACTION:
  /// Run KeepaliveHandler on an empty channel
  ///
  /// EXPECTED RESULT:
  /// Handler exits without signaling dead
  /// </summary>
  [Test]
  public async Task HandleKeepalive_EmptyChannel_ExitsWithoutTimeout()
  {
    var deadSignaled = false;
    var inputChannel = Channel.CreateUnbounded<MuxMessage>();
    inputChannel.Writer.Complete();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    try
    {
      await KeepaliveHandler.RunAsync(
        inputChannel.Reader,
        (_, _) => Task.CompletedTask,
        () => deadSignaled = true,
        NullLogger.Instance, cts.Token);
    }
    catch (OperationCanceledException) { }

    Assert.That(deadSignaled, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// The writer sends a keepalive but no response arrives within the timeout
  ///
  /// ACTION:
  /// Run KeepaliveHandler with short intervals on a channel that never delivers
  ///
  /// EXPECTED RESULT:
  /// signalDead is called
  /// </summary>
  [Test]
  public async Task HandleKeepalive_NoResponseWithinTimeout_SignalsDead()
  {
    var deadSignaled = false;
    var sendInterval = TimeSpan.FromMilliseconds(50);
    var receiveTimeout = TimeSpan.FromMilliseconds(150);

    var inputChannel = Channel.CreateUnbounded<MuxMessage>();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    try
    {
      await KeepaliveHandler.RunAsync(
        inputChannel.Reader,
        (_, _) => Task.CompletedTask,
        () => deadSignaled = true,
        NullLogger.Instance, sendInterval, receiveTimeout, cts.Token);
    }
    catch (OperationCanceledException) { }

    Assert.That(deadSignaled, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// sendInterval (150ms) > receiveTimeout (100ms), matching production ratios (15s/10s).
  /// A keepalive response arrives 50ms after the writer sends its probe.
  ///
  /// ACTION:
  /// Run KeepaliveHandler with production-like ratios, respond to the echo
  ///
  /// EXPECTED RESULT:
  /// signalDead is NOT called (the response arrived within the timeout window
  /// measured from when the echo was sent, not from last received)
  /// </summary>
  [Test]
  public async Task HandleKeepalive_ProductionRatios_ResponseInTime_DoesNotSignalDead()
  {
    var deadSignaled = false;
    var sendInterval = TimeSpan.FromMilliseconds(150);
    var receiveTimeout = TimeSpan.FromMilliseconds(100);

    var inputChannel = Channel.CreateUnbounded<MuxMessage>();
    var writeCount = 0;

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

    var handlerTask = KeepaliveHandler.RunAsync(
      inputChannel.Reader,
      (payload, ct2) =>
      {
        Interlocked.Increment(ref writeCount);
        var echoPayload = payload.ToArray();
        Task.Run(async () =>
        {
          await Task.Delay(50);
          try { await inputChannel.Writer.WriteAsync(new MuxMessage(0, echoPayload)); }
          catch (ChannelClosedException) { }
        });
        return Task.CompletedTask;
      },
      () => deadSignaled = true,
      NullLogger.Instance, sendInterval, receiveTimeout, cts.Token);

    try { await handlerTask; }
    catch (OperationCanceledException) { }

    Assert.That(deadSignaled, Is.False);
    Assert.That(writeCount, Is.LessThanOrEqualTo(5),
      "Expected only keepalive probes, not an echo loop");
  }
}
