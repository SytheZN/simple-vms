using Client.Core.Platform;

namespace Tests.Unit.Client.Mocks;

public sealed class MockNotificationService : INotificationService
{
  public List<(string Title, string Body, Guid? CameraId)> Calls { get; } = [];

  public Task ShowAsync(string title, string body, Guid? cameraId = null)
  {
    Calls.Add((title, body, cameraId));
    return Task.CompletedTask;
  }
}
