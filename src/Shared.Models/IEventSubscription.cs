namespace Shared.Models;

public interface IEventSubscription : IAsyncDisposable
{
  IAsyncEnumerable<CameraEvent> ReadEventsAsync(CancellationToken ct);
}
