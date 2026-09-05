using MessagePack;
using Server.Plugins;
using Server.Tunnel.Handlers;
using Shared.Models.Events;
using Shared.Protocol;

namespace Tests.Unit.Tunnel;

[TestFixture]
public class EventChannelHandlerTests
{
  /// <summary>
  /// SCENARIO:
  /// A client is attached to the tunnel's event channel and an event closes a duration event
  ///
  /// ACTION:
  /// Run EventChannelHandler and publish CameraEventRecorded with Ended set
  ///
  /// EXPECTED RESULT:
  /// The message is written as MessagePack with the channel flags carried in the frame header
  /// </summary>
  [Test]
  public async Task HandleEventChannel_Event_WritesMessagePackWithFlags()
  {
    var eventBus = new EventBus();
    var cameraId = Guid.NewGuid();
    var written = new List<(ushort Flags, byte[] Payload)>();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var handlerTask = EventChannelHandler.RunAsync(
      (flags, payload, _) =>
      {
        lock (written)
          written.Add((flags, payload.ToArray()));
        return Task.CompletedTask;
      },
      eventBus, cts.Token);

    await Task.Delay(50);

    await eventBus.PublishAsync(new CameraEventRecorded
    {
      Id = Guid.NewGuid(),
      CameraId = cameraId,
      Type = "motion",
      Timestamp = 6_000_000,
      Ended = true
    }, CancellationToken.None);

    await Task.Delay(100);
    await cts.CancelAsync();

    await handlerTask;

    Assert.That(written, Has.Count.EqualTo(1));

    var msg = MessagePackSerializer.Deserialize<EventChannelMessage>(
      written[0].Payload, ProtocolSerializer.Options);
    Assert.That(msg.CameraId, Is.EqualTo(cameraId));
    Assert.That(msg.Type, Is.EqualTo("motion"));
    Assert.That((EventChannelFlags)written[0].Flags, Is.EqualTo(EventChannelFlags.End));
  }
}
