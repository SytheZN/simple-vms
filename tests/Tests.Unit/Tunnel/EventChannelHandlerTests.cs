using System.Threading.Channels;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Plugins;
using Server.Tunnel.Handlers;
using Shared.Models;
using Shared.Models.Events;
using Shared.Protocol;

namespace Tests.Unit.Tunnel;

[TestFixture]
public class EventChannelHandlerTests
{
  /// <summary>
  /// SCENARIO:
  /// A MotionDetected event is published after the client subscribes
  ///
  /// ACTION:
  /// Run EventChannelHandler, publish MotionDetected on the event bus
  ///
  /// EXPECTED RESULT:
  /// An EventChannelMessage with type "motion" and Start flag is written via the write delegate
  /// </summary>
  [Test]
  public async Task HandleEventChannel_MotionDetected_WritesStartMessage()
  {
    var eventBus = new EventBus();
    var cameraId = Guid.NewGuid();
    var written = new List<(ushort Flags, ReadOnlyMemory<byte> Payload)>();

    var inputChannel = Channel.CreateUnbounded<MuxMessage>();
    inputChannel.Writer.Complete();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var handlerTask = EventChannelHandler.RunAsync(
      inputChannel.Reader,
      (flags, payload, ct) => { written.Add((flags, payload.ToArray())); return Task.CompletedTask; },
      eventBus, NullLogger.Instance, cts.Token);

    await Task.Delay(50);

    await eventBus.PublishAsync(new MotionDetected
    {
      CameraId = cameraId,
      Timestamp = 5_000_000
    }, CancellationToken.None);

    await Task.Delay(100);
    cts.Cancel();

    try { await handlerTask; }
    catch (OperationCanceledException) { }

    Assert.That(written, Has.Count.GreaterThanOrEqualTo(1));
    var msg = MessagePackSerializer.Deserialize<EventChannelMessage>(
      written[0].Payload, ProtocolSerializer.Options);
    Assert.That(msg.CameraId, Is.EqualTo(cameraId));
    Assert.That(msg.Type, Is.EqualTo("motion"));
    Assert.That((EventChannelFlags)written[0].Flags, Is.EqualTo(EventChannelFlags.Start));
  }

  /// <summary>
  /// SCENARIO:
  /// A MotionEnded event is published after the client subscribes
  ///
  /// ACTION:
  /// Run EventChannelHandler, publish MotionEnded on the event bus
  ///
  /// EXPECTED RESULT:
  /// An EventChannelMessage with type "motion" and End flag
  /// </summary>
  [Test]
  public async Task HandleEventChannel_MotionEnded_WritesEndMessage()
  {
    var eventBus = new EventBus();
    var cameraId = Guid.NewGuid();
    var written = new List<(ushort Flags, ReadOnlyMemory<byte> Payload)>();

    var inputChannel = Channel.CreateUnbounded<MuxMessage>();
    inputChannel.Writer.Complete();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var handlerTask = EventChannelHandler.RunAsync(
      inputChannel.Reader,
      (flags, payload, ct) => { written.Add((flags, payload.ToArray())); return Task.CompletedTask; },
      eventBus, NullLogger.Instance, cts.Token);

    await Task.Delay(50);

    await eventBus.PublishAsync(new MotionEnded
    {
      CameraId = cameraId,
      Timestamp = 6_000_000
    }, CancellationToken.None);

    await Task.Delay(100);
    cts.Cancel();

    try { await handlerTask; }
    catch (OperationCanceledException) { }

    Assert.That(written, Has.Count.GreaterThanOrEqualTo(1));
    var msg = MessagePackSerializer.Deserialize<EventChannelMessage>(
      written[0].Payload, ProtocolSerializer.Options);
    Assert.That(msg.Type, Is.EqualTo("motion"));
    Assert.That((EventChannelFlags)written[0].Flags, Is.EqualTo(EventChannelFlags.End));
  }

  /// <summary>
  /// SCENARIO:
  /// A CameraStatusChanged event is published after the client subscribes
  ///
  /// ACTION:
  /// Run EventChannelHandler, publish CameraStatusChanged on the event bus
  ///
  /// EXPECTED RESULT:
  /// An EventChannelMessage with type "status" and metadata containing status/profile
  /// </summary>
  [Test]
  public async Task HandleEventChannel_CameraStatusChanged_WritesStatusMessage()
  {
    var eventBus = new EventBus();
    var cameraId = Guid.NewGuid();
    var written = new List<(ushort Flags, ReadOnlyMemory<byte> Payload)>();

    var inputChannel = Channel.CreateUnbounded<MuxMessage>();
    inputChannel.Writer.Complete();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var handlerTask = EventChannelHandler.RunAsync(
      inputChannel.Reader,
      (flags, payload, ct) => { written.Add((flags, payload.ToArray())); return Task.CompletedTask; },
      eventBus, NullLogger.Instance, cts.Token);

    await Task.Delay(50);

    await eventBus.PublishAsync(new CameraStatusChanged
    {
      CameraId = cameraId,
      Profile = "main",
      Status = "online",
      Timestamp = 7_000_000
    }, CancellationToken.None);

    await Task.Delay(100);
    cts.Cancel();

    try { await handlerTask; }
    catch (OperationCanceledException) { }

    Assert.That(written, Has.Count.GreaterThanOrEqualTo(1));
    var msg = MessagePackSerializer.Deserialize<EventChannelMessage>(
      written[0].Payload, ProtocolSerializer.Options);
    Assert.That(msg.CameraId, Is.EqualTo(cameraId));
    Assert.That(msg.Type, Is.EqualTo("status"));
    Assert.That(msg.Metadata, Is.Not.Null);
    Assert.That(msg.Metadata!["status"], Is.EqualTo("online"));
    Assert.That(msg.Metadata["profile"], Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// No events published, cancellation fires
  ///
  /// ACTION:
  /// Run EventChannelHandler with no events and cancel
  ///
  /// EXPECTED RESULT:
  /// Handler exits cleanly, no messages written
  /// </summary>
  [Test]
  public async Task HandleEventChannel_NoEvents_ExitsCleanlyOnCancel()
  {
    var eventBus = new EventBus();
    var written = new List<(ushort Flags, ReadOnlyMemory<byte> Payload)>();

    var inputChannel = Channel.CreateUnbounded<MuxMessage>();
    inputChannel.Writer.Complete();

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

    try
    {
      await EventChannelHandler.RunAsync(
        inputChannel.Reader,
        (flags, payload, ct) => { written.Add((flags, payload.ToArray())); return Task.CompletedTask; },
        eventBus, NullLogger.Instance, cts.Token);
    }
    catch (OperationCanceledException) { }

    Assert.That(written, Is.Empty);
  }
}
