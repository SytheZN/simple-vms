using Client.Core.Streaming;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client;

[TestFixture]
public class LiveStreamServiceExtraTests
{
  /// <summary>
  /// SCENARIO:
  /// StateChanged fires with a non-Connected state (Disconnected, Connecting)
  ///
  /// ACTION:
  /// Subscribe, fire StateChanged(Disconnected)
  ///
  /// EXPECTED RESULT:
  /// No reconnect activity (OpenCount remains 1)
  /// </summary>
  [Test]
  public async Task StateChanged_NotConnected_DoesNotReconnect()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);

    await service.SubscribeAsync(Guid.NewGuid(), "main", CancellationToken.None);

    tunnel.FireStateChanged(ConnectionState.Disconnected);
    tunnel.FireStateChanged(ConnectionState.Connecting);
    await Task.Delay(50);

    Assert.That(tunnel.OpenCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// Reconnection swaps the live feed; subscribers should be notified via FeedReplaced
  ///
  /// ACTION:
  /// Subscribe one camera, fire StateChanged(Connected), capture FeedReplaced
  ///
  /// EXPECTED RESULT:
  /// FeedReplaced fires once with (oldFeed, newFeed) where the two refs differ
  /// </summary>
  [Test]
  public async Task Reconnect_FiresFeedReplaced()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);

    var oldFeed = await service.SubscribeAsync(Guid.NewGuid(), "main", CancellationToken.None);

    var replacements = new List<(IVideoFeed Old, IVideoFeed New)>();
    service.FeedReplaced += (o, n) => replacements.Add((o, n));

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(100);

    Assert.Multiple(() =>
    {
      Assert.That(replacements, Has.Count.EqualTo(1));
      Assert.That(replacements[0].Old, Is.SameAs(oldFeed));
      Assert.That(replacements[0].New, Is.Not.SameAs(oldFeed));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Multiple cameras have active subscriptions when reconnect happens
  ///
  /// ACTION:
  /// Subscribe to two cameras, fire StateChanged(Connected)
  ///
  /// EXPECTED RESULT:
  /// Both subscriptions get re-opened (OpenCount grows by 2)
  /// </summary>
  [Test]
  public async Task Reconnect_MultipleSubscriptions_AllResubscribe()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);

    await service.SubscribeAsync(Guid.NewGuid(), "main", CancellationToken.None);
    await service.SubscribeAsync(Guid.NewGuid(), "sub", CancellationToken.None);

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(150);

    Assert.That(tunnel.OpenCount, Is.EqualTo(4));
  }

  /// <summary>
  /// SCENARIO:
  /// Two reconnect events arrive in quick succession (e.g. brief connection
  /// flap). The first reconnect's CTS gets cancelled by the second; the
  /// service must survive without throwing
  ///
  /// ACTION:
  /// Subscribe, fire Connected, fire Connected again immediately
  ///
  /// EXPECTED RESULT:
  /// Service does not crash; subscription set was drained on the first event
  /// (so the second reconnect has nothing to do, but the cancellation race
  /// must not throw)
  /// </summary>
  [Test]
  public async Task Reconnect_TwiceInARow_DoesNotCrash()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);

    await service.SubscribeAsync(Guid.NewGuid(), "main", CancellationToken.None);

    Assert.DoesNotThrow(() =>
    {
      tunnel.FireStateChanged(ConnectionState.Connected);
      tunnel.FireStateChanged(ConnectionState.Connected);
    });
    await Task.Delay(150);
  }

  /// <summary>
  /// SCENARIO:
  /// Dispose detaches the StateChanged handler
  ///
  /// ACTION:
  /// Subscribe, Dispose, fire StateChanged(Connected)
  ///
  /// EXPECTED RESULT:
  /// No reconnect happens (OpenCount stays at 1)
  /// </summary>
  [Test]
  public async Task Dispose_DetachesStateHandler()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);
    await service.SubscribeAsync(Guid.NewGuid(), "main", CancellationToken.None);

    service.Dispose();
    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(50);

    Assert.That(tunnel.OpenCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// Unsubscribe removes the subscription so reconnect won't resurrect it
  ///
  /// ACTION:
  /// Subscribe, Unsubscribe, fire Connected
  ///
  /// EXPECTED RESULT:
  /// No new stream opens after the reconnect signal
  /// </summary>
  [Test]
  public async Task Unsubscribe_RemovesFromReconnectSet()
  {
    var tunnel = new FakeStreamTunnel();
    var service = new LiveStreamService(tunnel, NullLogger<LiveStreamService>.Instance);

    var feed = await service.SubscribeAsync(Guid.NewGuid(), "main", CancellationToken.None);
    await service.UnsubscribeAsync(feed, CancellationToken.None);

    tunnel.FireStateChanged(ConnectionState.Connected);
    await Task.Delay(50);

    Assert.That(tunnel.OpenCount, Is.EqualTo(1));
  }
}
