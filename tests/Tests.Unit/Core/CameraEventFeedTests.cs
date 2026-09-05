using Server.Core.Events;
using Server.Plugins;
using Shared.Models.Events;
using Shared.Protocol;

namespace Tests.Unit.Core;

[TestFixture]
public class CameraEventFeedTests
{
  /// <summary>
  /// SCENARIO:
  /// An event has been written to history while a client is attached to the feed
  ///
  /// ACTION:
  /// Run the feed and publish CameraEventRecorded
  ///
  /// EXPECTED RESULT:
  /// The row reaches the client intact, identifier included, so a later query returns the same
  /// event the client was shown
  /// </summary>
  [Test]
  public async Task Feed_RecordedEvent_EmitsTheRow()
  {
    var id = Guid.NewGuid();
    var cameraId = Guid.NewGuid();

    var emitted = await CollectAsync(bus => bus.PublishAsync(new CameraEventRecorded
    {
      Id = id,
      CameraId = cameraId,
      Type = "motion",
      Timestamp = 5_000_000,
      Metadata = new Dictionary<string, string> { ["active"] = "True" }
    }, CancellationToken.None));

    Assert.That(emitted, Has.Count.EqualTo(1));
    Assert.That(emitted[0].Message.Id, Is.EqualTo(id));
    Assert.That(emitted[0].Message.CameraId, Is.EqualTo(cameraId));
    Assert.That(emitted[0].Message.Type, Is.EqualTo("motion"));
    Assert.That(emitted[0].Message.Metadata!["active"], Is.EqualTo("True"));
    Assert.That(emitted[0].Flags, Is.EqualTo(EventChannelFlags.Start));
  }

  /// <summary>
  /// SCENARIO:
  /// A recorded event closes a duration event rather than opening one
  ///
  /// ACTION:
  /// Run the feed and publish CameraEventRecorded with Ended set
  ///
  /// EXPECTED RESULT:
  /// The message is flagged End
  /// </summary>
  [Test]
  public async Task Feed_RecordedEventEnded_EmitsEnd()
  {
    var emitted = await CollectAsync(bus => bus.PublishAsync(new CameraEventRecorded
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      Timestamp = 6_000_000,
      Ended = true
    }, CancellationToken.None));

    Assert.That(emitted, Has.Count.EqualTo(1));
    Assert.That(emitted[0].Flags, Is.EqualTo(EventChannelFlags.End));
  }

  /// <summary>
  /// SCENARIO:
  /// A pipeline reports a camera offline with a reason
  ///
  /// ACTION:
  /// Run the feed and publish CameraStatusChanged
  ///
  /// EXPECTED RESULT:
  /// A "status" message whose metadata carries status, profile and reason
  /// </summary>
  [Test]
  public async Task Feed_CameraStatusChanged_CarriesStatusMetadata()
  {
    var emitted = await CollectAsync(bus => bus.PublishAsync(new CameraStatusChanged
    {
      CameraId = Guid.NewGuid(),
      Profile = "main",
      Status = "offline",
      Reason = "disconnected",
      Timestamp = 7_000_000
    }, CancellationToken.None));

    Assert.That(emitted, Has.Count.EqualTo(1));
    Assert.That(emitted[0].Message.Type, Is.EqualTo("__status"));
    Assert.That(emitted[0].Message.Metadata, Is.Not.Null);
    Assert.That(emitted[0].Message.Metadata!["status"], Is.EqualTo("offline"));
    Assert.That(emitted[0].Message.Metadata["profile"], Is.EqualTo("main"));
    Assert.That(emitted[0].Message.Metadata["reason"], Is.EqualTo("disconnected"));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is removed, which leaves no history row to carry
  ///
  /// ACTION:
  /// Run the feed and publish CameraRemoved
  ///
  /// EXPECTED RESULT:
  /// A "removed" message naming the camera, so a client stops showing it
  /// </summary>
  [Test]
  public async Task Feed_CameraRemoved_EmitsRemoved()
  {
    var cameraId = Guid.NewGuid();

    var emitted = await CollectAsync(bus => bus.PublishAsync(new CameraRemoved
    {
      CameraId = cameraId,
      Name = "Porch",
      Timestamp = 9_000_000
    }, CancellationToken.None));

    Assert.That(emitted, Has.Count.EqualTo(1));
    Assert.That(emitted[0].Message.Type, Is.EqualTo("__removed"));
    Assert.That(emitted[0].Message.CameraId, Is.EqualTo(cameraId));
  }

  /// <summary>
  /// SCENARIO:
  /// Motion is processed, which publishes MotionDetected for internal consumers alongside the row
  ///
  /// ACTION:
  /// Run the feed and publish MotionDetected
  ///
  /// EXPECTED RESULT:
  /// Nothing is emitted, so a client sees one message per event rather than two
  /// </summary>
  [Test]
  public async Task Feed_MotionDetected_IsNotCarried()
  {
    var emitted = await CollectAsync(bus => bus.PublishAsync(new MotionDetected
    {
      CameraId = Guid.NewGuid(),
      Timestamp = 5_000_000
    }, CancellationToken.None));

    Assert.That(emitted, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A client attaches to the feed and no events are published before it goes away
  ///
  /// ACTION:
  /// Run the feed and cancel without publishing
  ///
  /// EXPECTED RESULT:
  /// The feed exits without emitting anything
  /// </summary>
  [Test]
  public async Task Feed_NoEvents_ExitsCleanlyOnCancel()
  {
    var emitted = await CollectAsync(_ => Task.CompletedTask);

    Assert.That(emitted, Is.Empty);
  }

  private static async Task<List<(EventChannelMessage Message, EventChannelFlags Flags)>>
    CollectAsync(Func<EventBus, Task> publish)
  {
    var eventBus = new EventBus();
    var emitted = new List<(EventChannelMessage, EventChannelFlags)>();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var feed = CameraEventFeed.RunAsync(
      (message, flags, _) =>
      {
        lock (emitted)
          emitted.Add((message, flags));
        return Task.CompletedTask;
      },
      eventBus, cts.Token);

    await Task.Delay(50);
    await publish(eventBus);
    await Task.Delay(100);
    await cts.CancelAsync();

    await feed.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    lock (emitted)
      return [.. emitted];
  }
}
