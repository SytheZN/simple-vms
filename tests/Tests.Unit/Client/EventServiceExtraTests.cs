using Client.Core.Events;
using Client.Core.Tunnel;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client;

[TestFixture]
public class EventServiceExtraTests
{
  /// <summary>
  /// SCENARIO:
  /// Tunnel emits StateChanged(Connected) while the service is running
  ///
  /// ACTION:
  /// Start the service, fire Connected
  ///
  /// EXPECTED RESULT:
  /// A new event-channel stream is opened (resubscribe path)
  /// </summary>
  [Test]
  public async Task StateChanged_Connected_Resubscribes()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    await service.StartAsync(CancellationToken.None);
    var initialOpens = tunnel.OpenCount;

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(100);

    Assert.That(tunnel.OpenCount, Is.GreaterThan(initialOpens));

    await service.StopAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Tunnel emits StateChanged(Connected) while the service is NOT running
  ///
  /// ACTION:
  /// Construct the service, fire Connected without StartAsync
  ///
  /// EXPECTED RESULT:
  /// No stream is opened (the running guard is honoured)
  /// </summary>
  [Test]
  public async Task StateChanged_NotRunning_DoesNotResubscribe()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(50);

    Assert.That(tunnel.OpenCount, Is.Zero);

    await service.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Tunnel emits a non-Connected state
  ///
  /// ACTION:
  /// Start, fire Disconnected
  ///
  /// EXPECTED RESULT:
  /// No additional stream open occurs
  /// </summary>
  [Test]
  public async Task StateChanged_Disconnected_NoOp()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    await service.StartAsync(CancellationToken.None);
    var opens = tunnel.OpenCount;

    tunnel.FireStateChanged(ConnectionState.Disconnected);
    await Task.Delay(50);

    Assert.That(tunnel.OpenCount, Is.EqualTo(opens));

    await service.StopAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// StartAsync called twice in a row replaces the existing session
  ///
  /// ACTION:
  /// Start twice
  ///
  /// EXPECTED RESULT:
  /// Two OpenStream calls are observed; no exception
  /// </summary>
  [Test]
  public async Task Start_TwiceInARow_ReplacesSession()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    await service.StartAsync(CancellationToken.None);
    await service.StartAsync(CancellationToken.None);

    Assert.That(tunnel.OpenCount, Is.EqualTo(2));

    await service.StopAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// A malformed payload arrives that fails MessagePack deserialisation
  ///
  /// ACTION:
  /// Start, push raw garbage bytes, then a valid event after
  ///
  /// EXPECTED RESULT:
  /// The bad message is logged and skipped; the subsequent valid message
  /// still surfaces via OnEvent (read loop survives the exception)
  /// </summary>
  [Test]
  public async Task ReadLoop_BadPayload_SkippedAndLoopContinues()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    var received = new List<EventChannelMessage>();
    service.OnEvent += (m, _) => received.Add(m);

    await service.StartAsync(CancellationToken.None);

    await tunnel.LastChannel!.Writer.WriteAsync(
      new MuxMessage(0, new byte[] { 0xFF, 0xFE, 0xFD }));

    var goodMsg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 1
    };
    var goodPayload = MessagePackSerializer.Serialize(goodMsg, ProtocolSerializer.Options);
    await tunnel.LastChannel.Writer.WriteAsync(
      new MuxMessage((ushort)EventChannelFlags.Start, goodPayload));

    await Task.Delay(100);

    Assert.That(received, Has.Count.EqualTo(1));
    Assert.That(received[0].Type, Is.EqualTo("motion"));

    await service.StopAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// DisposeAsync is called on a never-started service
  ///
  /// ACTION:
  /// Construct then DisposeAsync immediately
  ///
  /// EXPECTED RESULT:
  /// Completes without throwing
  /// </summary>
  [Test]
  public async Task Dispose_NeverStarted_NoOp()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    await service.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// DisposeAsync detaches the StateChanged handler
  ///
  /// ACTION:
  /// Start, Dispose, then fire StateChanged
  ///
  /// EXPECTED RESULT:
  /// Resubscribe is not triggered after dispose
  /// </summary>
  [Test]
  public async Task Dispose_DetachesStateChangedHandler()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new EventService(tunnel, NullLogger<EventService>.Instance);

    await service.StartAsync(CancellationToken.None);
    await service.DisposeAsync();
    var opens = tunnel.OpenCount;

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(50);

    Assert.That(tunnel.OpenCount, Is.EqualTo(opens));
  }
}
