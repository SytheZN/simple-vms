using Client.Core.Streaming;
using MessagePack;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client;

[TestFixture]
public class PlaybackServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// A playback request is opened
  ///
  /// ACTION:
  /// Call StartAsync and verify the request payload
  ///
  /// EXPECTED RESULT:
  /// The PlaybackRequestMessage contains the correct cameraId, profile, from, and to
  /// </summary>
  [Test]
  public async Task Start_SendsCorrectRequest()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new PlaybackService(tunnel);
    var cameraId = Guid.NewGuid();

    var feed = await service.StartAsync(cameraId, "main", 1_000_000, 2_000_000, CancellationToken.None);

    Assert.That(tunnel.LastPayload, Is.Not.Null);
    var request = MessagePackSerializer.Deserialize<PlaybackRequestMessage>(
      tunnel.LastPayload!, ProtocolSerializer.Options);
    Assert.That(request.CameraId, Is.EqualTo(cameraId));
    Assert.That(request.Profile, Is.EqualTo("main"));
    Assert.That(request.From, Is.EqualTo(1_000_000UL));
    Assert.That(request.To, Is.EqualTo(2_000_000UL));

    await service.StopAsync(feed, CancellationToken.None);
  }

  /// <summary>
  /// SCENARIO:
  /// A seek operation is performed on an active playback feed
  ///
  /// ACTION:
  /// Start playback, then seek to a new timestamp
  ///
  /// EXPECTED RESULT:
  /// The original stream is closed and a new one is opened with the new timestamp
  /// </summary>
  [Test]
  public async Task Seek_ClosesOldAndOpensNew()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new PlaybackService(tunnel);
    var cameraId = Guid.NewGuid();

    var feed1 = await service.StartAsync(cameraId, "main", 1_000_000, null, CancellationToken.None);
    Assert.That(tunnel.OpenCount, Is.EqualTo(1));

    var feed2 = await service.SeekAsync(feed1, 5_000_000, CancellationToken.None);
    Assert.That(tunnel.OpenCount, Is.EqualTo(2));

    var request = MessagePackSerializer.Deserialize<PlaybackRequestMessage>(
      tunnel.LastPayload!, ProtocolSerializer.Options);
    Assert.That(request.From, Is.EqualTo(5_000_000UL));
    Assert.That(request.CameraId, Is.EqualTo(cameraId));

    await service.StopAsync(feed2, CancellationToken.None);
  }

  /// <summary>
  /// SCENARIO:
  /// A playback feed receives a gap message
  ///
  /// ACTION:
  /// Start playback, feed a gap status message
  ///
  /// EXPECTED RESULT:
  /// VideoFeed fires OnGap with the correct from/to timestamps
  /// </summary>
  [Test]
  public async Task Playback_GapMessage_FiresOnGap()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new PlaybackService(tunnel);

    var feed = await service.StartAsync(Guid.NewGuid(), "main", 1_000_000, null, CancellationToken.None);

    GapStatus? receivedGap = null;
    feed.OnGap += gap => receivedGap = gap;

    var gapMsg = StreamMessageWriter.SerializeGap(2_000_000, 3_000_000);
    await tunnel.LastChannel!.Writer.WriteAsync(new MuxMessage(0, gapMsg));

    await Task.Delay(50);

    Assert.That(receivedGap, Is.Not.Null);
    Assert.That(receivedGap!.Value.From, Is.EqualTo(2_000_000UL));
    Assert.That(receivedGap.Value.To, Is.EqualTo(3_000_000UL));

    await service.StopAsync(feed, CancellationToken.None);
  }
}
