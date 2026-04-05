using Shared.Protocol;

namespace Client.Core.Events;

public interface IEventService
{
  event Action<EventChannelMessage, EventChannelFlags>? OnEvent;
  Task StartAsync(CancellationToken ct);
  Task StopAsync();
}
