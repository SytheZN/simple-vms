namespace Client.Core.Platform;

public interface INotificationService
{
  Task ShowAsync(string title, string body, Guid? cameraId = null);
}
