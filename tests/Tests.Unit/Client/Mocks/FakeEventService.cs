using Client.Core.Events;
using Shared.Protocol;

namespace Tests.Unit.Client.Mocks;

public sealed class FakeEventService : IEventService
{
  public event Action<EventChannelMessage, EventChannelFlags>? OnEvent;

  public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
  public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

  public void Fire(EventChannelMessage msg, EventChannelFlags flags) =>
    OnEvent?.Invoke(msg, flags);
}
