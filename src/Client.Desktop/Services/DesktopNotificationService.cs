using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Client.Core.Platform;
using System.Diagnostics.CodeAnalysis;

namespace Client.Desktop.Services;

[ExcludeFromCodeCoverage]
public sealed class DesktopNotificationService : INotificationService
{
  private WindowNotificationManager? _manager;

  public void SetWindow(Window window)
  {
    _manager = new WindowNotificationManager(window)
    {
      Position = NotificationPosition.TopRight,
      MaxItems = 3
    };
  }

  public Task ShowAsync(string title, string body, Guid? cameraId = null)
  {
    Dispatcher.UIThread.Post(() =>
    {
      _manager?.Show(new Notification(title, body, NotificationType.Information));
    });
    return Task.CompletedTask;
  }
}
