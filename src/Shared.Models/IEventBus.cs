using Shared.Models.Events;

namespace Shared.Models;

public interface IEventBus
{
  Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent;
  IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct) where T : ISystemEvent;
}
