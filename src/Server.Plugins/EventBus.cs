using System.Threading.Channels;
using Shared.Models;
using Shared.Models.Events;

namespace Server.Plugins;

public sealed class EventBus : IEventBus
{
  private readonly Lock _lock = new();
  private readonly Dictionary<Type, List<object>> _subscribers = [];

  public async Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent
  {
    List<object> channels;
    lock (_lock)
    {
      if (!_subscribers.TryGetValue(typeof(T), out var list))
        return;
      channels = [.. list];
    }

    foreach (var ch in channels)
    {
      var typed = (Channel<T>)ch;
      await typed.Writer.WriteAsync(evt, ct);
    }
  }

  public async IAsyncEnumerable<T> SubscribeAsync<T>(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    where T : ISystemEvent
  {
    var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(256)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = true,
      SingleWriter = false
    });

    lock (_lock)
    {
      if (!_subscribers.TryGetValue(typeof(T), out var list))
      {
        list = [];
        _subscribers[typeof(T)] = list;
      }
      list.Add(channel);
    }

    try
    {
      while (true)
      {
        bool available;
        try { available = await channel.Reader.WaitToReadAsync(ct); }
        catch (OperationCanceledException) { break; }
        if (!available) break;
        while (channel.Reader.TryRead(out var item))
          yield return item;
      }
    }
    finally
    {
      lock (_lock)
      {
        if (_subscribers.TryGetValue(typeof(T), out var list))
        {
          list.Remove(channel);
          if (list.Count == 0)
            _subscribers.Remove(typeof(T));
        }
      }
    }
  }
}
