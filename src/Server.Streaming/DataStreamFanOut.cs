using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Server.Streaming;

public sealed class DataStreamFanOut<T> : IDataStream<T>, IAsyncDisposable where T : IDataUnit
{
  private readonly List<Channel<T>> _subscribers = [];
  private readonly Lock _lock = new();
  private int _demandCount;

  public StreamInfo Info { get; }
  public Type FrameType => typeof(T);
  public int SubscriberCount { get { lock (_lock) return _subscribers.Count; } }
  public Action? OnDemand { get; set; }
  public Action? OnEmpty { get; set; }
  public ILogger? Logger { get; set; }

  public DataStreamFanOut(StreamInfo info)
  {
    Info = info;
  }

  public void Write(T item)
  {
    Channel<T>[] snapshot;
    lock (_lock)
      snapshot = [.. _subscribers];

    foreach (var channel in snapshot)
      channel.Writer.TryWrite(item);
  }

  public ChannelDataStream<T> Subscribe(int capacity = 256)
  {
    var channel = CreateChannel(capacity);

    Action? onDemand = null;
    lock (_lock)
    {
      _subscribers.Add(channel);
      _demandCount++;
      if (_demandCount == 1)
        onDemand = OnDemand;
    }
    onDemand?.Invoke();

    return new ChannelDataStream<T>(Info, channel.Reader, () => Unsubscribe(channel, demand: true));
  }

  public ChannelDataStream<T> SubscribePassive(int capacity = 256)
  {
    var channel = CreateChannel(capacity);

    lock (_lock)
      _subscribers.Add(channel);

    return new ChannelDataStream<T>(Info, channel.Reader, () => Unsubscribe(channel, demand: false));
  }

  private static Channel<T> CreateChannel(int capacity) =>
    Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = true,
      SingleWriter = false
    });

  private void Unsubscribe(Channel<T> channel, bool demand)
  {
    Action? onEmpty = null;
    lock (_lock)
    {
      _subscribers.Remove(channel);
      if (demand)
      {
        _demandCount--;
        if (_demandCount == 0)
          onEmpty = OnEmpty;
      }
    }
    onEmpty?.Invoke();
  }

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    using var sub = Subscribe();
    await foreach (var item in sub.ReadAsync(ct))
      yield return item;
  }

  public ValueTask DisposeAsync()
  {
    lock (_lock)
    {
      foreach (var channel in _subscribers)
        channel.Writer.TryComplete();
      _subscribers.Clear();
    }
    return ValueTask.CompletedTask;
  }
}

public sealed class ChannelDataStream<T> : IDataStream<T>, IDisposable where T : IDataUnit
{
  private readonly ChannelReader<T> _reader;
  private readonly Action _onUnsubscribe;
  private int _disposed;

  public StreamInfo Info { get; }
  public Type FrameType => typeof(T);

  public ChannelDataStream(StreamInfo info, ChannelReader<T> reader, Action onUnsubscribe)
  {
    Info = info;
    _reader = reader;
    _onUnsubscribe = onUnsubscribe;
  }

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var item in _reader.ReadAllAsync(ct))
      yield return item;
  }

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 0)
      _onUnsubscribe();
  }
}
