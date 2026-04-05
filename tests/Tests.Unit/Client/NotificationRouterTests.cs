using Client.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client;

[TestFixture]
public class NotificationRouterTests
{
  /// <summary>
  /// SCENARIO:
  /// A Start event matches an enabled rule with matching camera and type
  ///
  /// ACTION:
  /// Fire the event through the event service
  ///
  /// EXPECTED RESULT:
  /// NotificationService.ShowAsync is called once
  /// </summary>
  [Test]
  public void OnEvent_MatchingRule_SendsNotification()
  {
    var eventService = new FakeEventService();
    var notifications = new MockNotificationService();
    var router = new NotificationRouter(eventService, notifications, NullLogger<NotificationRouter>.Instance);

    var cameraId = Guid.NewGuid();
    router.UpdateRules([new NotificationRule(cameraId, "motion", true)]);

    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = cameraId,
      Type = "motion",
      StartTime = 1_000_000
    };
    eventService.Fire(msg, EventChannelFlags.Start);

    Assert.That(notifications.Calls, Has.Count.EqualTo(1));
    Assert.That(notifications.Calls[0].CameraId, Is.EqualTo(cameraId));
  }

  /// <summary>
  /// SCENARIO:
  /// An End event arrives (not Start)
  ///
  /// ACTION:
  /// Fire the event through the event service
  ///
  /// EXPECTED RESULT:
  /// No notification is sent
  /// </summary>
  [Test]
  public void OnEvent_EndFlag_NoNotification()
  {
    var eventService = new FakeEventService();
    var notifications = new MockNotificationService();
    var router = new NotificationRouter(eventService, notifications, NullLogger<NotificationRouter>.Instance);

    router.UpdateRules([new NotificationRule(null, null, true)]);

    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 1_000_000
    };
    eventService.Fire(msg, EventChannelFlags.End);

    Assert.That(notifications.Calls, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A Start event arrives but the matching rule is disabled
  ///
  /// ACTION:
  /// Fire the event through the event service
  ///
  /// EXPECTED RESULT:
  /// No notification is sent
  /// </summary>
  [Test]
  public void OnEvent_DisabledRule_NoNotification()
  {
    var eventService = new FakeEventService();
    var notifications = new MockNotificationService();
    var router = new NotificationRouter(eventService, notifications, NullLogger<NotificationRouter>.Instance);

    router.UpdateRules([new NotificationRule(null, "motion", false)]);

    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 1_000_000
    };
    eventService.Fire(msg, EventChannelFlags.Start);

    Assert.That(notifications.Calls, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A Start event arrives but the camera ID doesn't match the rule
  ///
  /// ACTION:
  /// Fire the event through the event service
  ///
  /// EXPECTED RESULT:
  /// No notification is sent
  /// </summary>
  [Test]
  public void OnEvent_WrongCamera_NoNotification()
  {
    var eventService = new FakeEventService();
    var notifications = new MockNotificationService();
    var router = new NotificationRouter(eventService, notifications, NullLogger<NotificationRouter>.Instance);

    router.UpdateRules([new NotificationRule(Guid.NewGuid(), null, true)]);

    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 1_000_000
    };
    eventService.Fire(msg, EventChannelFlags.Start);

    Assert.That(notifications.Calls, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A Start event arrives but the type doesn't match the rule
  ///
  /// ACTION:
  /// Fire the event through the event service
  ///
  /// EXPECTED RESULT:
  /// No notification is sent
  /// </summary>
  [Test]
  public void OnEvent_WrongType_NoNotification()
  {
    var eventService = new FakeEventService();
    var notifications = new MockNotificationService();
    var router = new NotificationRouter(eventService, notifications, NullLogger<NotificationRouter>.Instance);

    router.UpdateRules([new NotificationRule(null, "tamper", true)]);

    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 1_000_000
    };
    eventService.Fire(msg, EventChannelFlags.Start);

    Assert.That(notifications.Calls, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A wildcard rule (null camera, null type) is enabled
  ///
  /// ACTION:
  /// Fire a Start event with any camera and type
  ///
  /// EXPECTED RESULT:
  /// Notification is sent
  /// </summary>
  [Test]
  public void OnEvent_WildcardRule_SendsNotification()
  {
    var eventService = new FakeEventService();
    var notifications = new MockNotificationService();
    var router = new NotificationRouter(eventService, notifications, NullLogger<NotificationRouter>.Instance);

    router.UpdateRules([new NotificationRule(null, null, true)]);

    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "status",
      StartTime = 1_000_000
    };
    eventService.Fire(msg, EventChannelFlags.Start);

    Assert.That(notifications.Calls, Has.Count.EqualTo(1));
  }
}
