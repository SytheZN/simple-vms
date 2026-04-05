namespace Client.Core.Events;

public sealed record NotificationRule(
  Guid? CameraId,
  string? EventType,
  bool Enabled);
