using Client.Core.Streaming;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client;

[TestFixture]
public class LiveStreamServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// A live stream subscription receives an init segment followed by a GOP
  ///
  /// ACTION:
  /// Subscribe, feed init and gop messages through the channel
  ///
  /// EXPECTED RESULT:
  /// VideoFeed fires OnInit with the init data and OnGop with the gop data
  /// </summary>
  [Test]
  public async Task Subscribe_InitAndGop_FiresEvents()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);
    var cameraId = Guid.NewGuid();

    var feed = await service.SubscribeAsync(cameraId, "main", CancellationToken.None);

    ReadOnlyMemory<byte>? receivedInit = null;
    GopMessage? receivedGop = null;
    feed.OnInit += data => receivedInit = data;
    feed.OnGop += gop => receivedGop = gop;

    var initData = new byte[] { 0x00, 0x00, 0x00, 0x1C };
    var initMsg = StreamMessageWriter.SerializeInit("main", initData);
    await tunnel.LastChannel!.Writer.WriteAsync(new MuxMessage(0, initMsg));

    var gopData = new byte[] { 0xAA, 0xBB, 0xCC };
    var gopMsg = StreamMessageWriter.SerializeGop(GopFlags.Begin, "main", 1_000_000, gopData);
    await tunnel.LastChannel.Writer.WriteAsync(new MuxMessage(0, gopMsg));

    await Task.Delay(50);

    Assert.That(receivedInit, Is.Not.Null);
    Assert.That(receivedInit!.Value.ToArray(), Is.EqualTo(initData));
    Assert.That(receivedGop, Is.Not.Null);
    Assert.That(receivedGop!.Value.Timestamp, Is.EqualTo(1_000_000UL));

    await service.UnsubscribeAsync(feed, CancellationToken.None);
  }

  /// <summary>
  /// SCENARIO:
  /// A live stream subscription receives a status message
  ///
  /// ACTION:
  /// Subscribe, feed a Status(Ack) message
  ///
  /// EXPECTED RESULT:
  /// VideoFeed fires OnStatus with Ack
  /// </summary>
  [Test]
  public async Task Subscribe_StatusMessage_FiresOnStatus()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);

    var feed = await service.SubscribeAsync(Guid.NewGuid(), "main", CancellationToken.None);

    StreamStatus? receivedStatus = null;
    feed.OnStatus += s => receivedStatus = s;

    var statusMsg = StreamMessageWriter.SerializeStatus(StreamStatus.Ack);
    await tunnel.LastChannel!.Writer.WriteAsync(new MuxMessage(0, statusMsg));

    await Task.Delay(50);

    Assert.That(receivedStatus, Is.EqualTo(StreamStatus.Ack));

    await service.UnsubscribeAsync(feed, CancellationToken.None);
  }

  /// <summary>
  /// SCENARIO:
  /// The tunnel reconnects after disconnection
  ///
  /// ACTION:
  /// Subscribe, then fire StateChanged(Connected)
  ///
  /// EXPECTED RESULT:
  /// A new stream is opened (resubscribe)
  /// </summary>
  [Test]
  public async Task Reconnect_ResubscribesActiveFeeds()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);

    await service.SubscribeAsync(Guid.NewGuid(), "main", CancellationToken.None);
    var firstChannel = tunnel.LastChannel;

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(100);

    Assert.That(tunnel.OpenCount, Is.EqualTo(2));
    Assert.That(tunnel.LastChannel, Is.Not.SameAs(firstChannel));
  }
}
