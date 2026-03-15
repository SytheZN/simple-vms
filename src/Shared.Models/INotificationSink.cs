namespace Shared.Models;

public interface INotificationSink
{
  string SinkId { get; }
  Task SendAsync(CameraEvent evt, CancellationToken ct);
}
