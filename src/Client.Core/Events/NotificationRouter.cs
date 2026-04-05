using Client.Core.Platform;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Events;

public sealed class NotificationRouter : IDisposable
{
  private readonly IEventService _events;
  private readonly INotificationService _notifications;
  private readonly ILogger<NotificationRouter> _logger;
  private readonly Lock _lock = new();
  private IReadOnlyList<NotificationRule> _rules = [];

  public NotificationRouter(
    IEventService events,
    INotificationService notifications,
    ILogger<NotificationRouter> logger)
  {
    _events = events;
    _notifications = notifications;
    _logger = logger;
    _events.OnEvent += OnEvent;
  }

  public void UpdateRules(IReadOnlyList<NotificationRule> rules)
  {
    lock (_lock) _rules = rules;
  }

  private void OnEvent(EventChannelMessage msg, EventChannelFlags flags)
  {
    if ((flags & EventChannelFlags.Start) == 0)
      return;

    IReadOnlyList<NotificationRule> rules;
    lock (_lock) rules = _rules;

    foreach (var rule in rules)
    {
      if (!rule.Enabled)
        continue;
      if (rule.CameraId != null && rule.CameraId != msg.CameraId)
        continue;
      if (rule.EventType != null && rule.EventType != msg.Type)
        continue;

      _ = SendNotificationAsync(msg.Type, msg.CameraId);
      return;
    }
  }

  private async Task SendNotificationAsync(string type, Guid cameraId)
  {
    try { await _notifications.ShowAsync(type, $"Camera event: {type}", cameraId); }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Notification delivery failed for camera {CameraId}", cameraId);
    }
  }

  public void Dispose()
  {
    _events.OnEvent -= OnEvent;
  }
}
