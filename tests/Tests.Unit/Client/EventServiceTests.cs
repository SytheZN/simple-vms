using Client.Core.Events;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client;

[TestFixture]
public class EventServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// The event channel receives an EventChannelMessage with Start flag
  ///
  /// ACTION:
  /// Start the event service, feed a message through the channel
  ///
  /// EXPECTED RESULT:
  /// OnEvent fires with the message and Start flag
  /// </summary>
  [Test]
  public async Task Start_ReceivesEvent_FiresOnEvent()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    EventChannelMessage? received = null;
    EventChannelFlags? receivedFlags = null;
    service.OnEvent += (msg, flags) =>
    {
      received = msg;
      receivedFlags = flags;
    };

    await service.StartAsync(CancellationToken.None);

    var cameraId = Guid.NewGuid();
    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = cameraId,
      Type = "motion",
      StartTime = 1_000_000
    };
    var payload = MessagePackSerializer.Serialize(msg, ProtocolSerializer.Options);
    await tunnel.LastChannel!.Writer.WriteAsync(
      new MuxMessage((ushort)EventChannelFlags.Start, payload));

    await Task.Delay(50);

    Assert.That(received, Is.Not.Null);
    Assert.That(received!.CameraId, Is.EqualTo(cameraId));
    Assert.That(received.Type, Is.EqualTo("motion"));
    Assert.That(receivedFlags, Is.EqualTo(EventChannelFlags.Start));

    await service.StopAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Multiple events arrive in sequence
  ///
  /// ACTION:
  /// Start the event service, feed two messages
  ///
  /// EXPECTED RESULT:
  /// OnEvent fires for each message
  /// </summary>
  [Test]
  public async Task Start_MultipleEvents_AllDelivered()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    var receivedCount = 0;
    service.OnEvent += (_, _) => Interlocked.Increment(ref receivedCount);

    await service.StartAsync(CancellationToken.None);

    for (var i = 0; i < 3; i++)
    {
      var msg = new EventChannelMessage
      {
        Id = Guid.NewGuid(),
        CameraId = Guid.NewGuid(),
        Type = "status",
        StartTime = (ulong)(i * 1_000_000)
      };
      var payload = MessagePackSerializer.Serialize(msg, ProtocolSerializer.Options);
      await tunnel.LastChannel!.Writer.WriteAsync(
        new MuxMessage((ushort)EventChannelFlags.Start, payload));
    }

    await Task.Delay(100);

    Assert.That(receivedCount, Is.EqualTo(3));

    await service.StopAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// The event service is stopped
  ///
  /// ACTION:
  /// Start then stop the service
  ///
  /// EXPECTED RESULT:
  /// No exceptions, service stops cleanly
  /// </summary>
  [Test]
  public async Task Stop_CleansUpCleanly()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    await service.StartAsync(CancellationToken.None);
    await service.StopAsync();

    Assert.Pass();
  }
}
