using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Server.Streaming;

public sealed class DataStreamFanOut<T> : IDataStream<T>, IDataStreamFanOut where T : IDataUnit
{
  private readonly List<Entry> _subscribers = [];
  private readonly List<T> _gopCache = [];
  private readonly Lock _lock = new();
  private Channel<T>[]? _snapshot;
  private bool _cachingHeadersOnly;

  public const int MaxCachedUnits = 256;

  public StreamInfo Info { get; }
  public Type FrameType => typeof(T);
  public int SubscriberCount { get { lock (_lock) return _subscribers.Count; } }
  public Action? Changed { get; set; }
  public ILogger? Logger { get; set; }

  public DataStreamFanOut(StreamInfo info)
  {
    Info = info;
  }

  public int GetDemand()
  {
    lock (_lock)
      return _subscribers.Count(s => s.Demands);
  }

  public void Write(T item)
  {
    Channel<T>[] snapshot;
    lock (_lock)
    {
      if (item.IsSyncPoint)
      {
        TrimCacheToCurrentHeaders();
        _cachingHeadersOnly = false;
      }
      Cache(item);
      snapshot = _snapshot ??= [.. _subscribers.Select(s => s.Channel)];
    }

    foreach (var channel in snapshot)
      channel.Writer.TryWrite(item);
  }

  private void Cache(T item)
  {
    if (_cachingHeadersOnly && !item.IsHeader)
      return;

    _gopCache.Add(item);
    if (_gopCache.Count <= MaxCachedUnits)
      return;

    if (_cachingHeadersOnly)
    {
      _gopCache.RemoveAt(0);
      return;
    }

    _gopCache.RemoveAll(cached => !cached.IsHeader);
    _cachingHeadersOnly = true;
  }

  private void TrimCacheToCurrentHeaders()
  {
    var freshHeadersStart = _gopCache.Count;
    while (freshHeadersStart > 0 && _gopCache[freshHeadersStart - 1].IsHeader)
      freshHeadersStart--;

    if (freshHeadersStart < _gopCache.Count)
    {
      _gopCache.RemoveRange(0, freshHeadersStart);
      return;
    }

    var retainedHeadersEnd = 0;
    while (retainedHeadersEnd < _gopCache.Count && _gopCache[retainedHeadersEnd].IsHeader)
      retainedHeadersEnd++;
    _gopCache.RemoveRange(retainedHeadersEnd, _gopCache.Count - retainedHeadersEnd);
  }

  public ChannelDataStream<T> Subscribe(int capacity = 256) =>
    Add(capacity, demands: true);

  public ChannelDataStream<T> SubscribePassive(int capacity = 256) =>
    Add(capacity, demands: false);

  private ChannelDataStream<T> Add(int capacity, bool demands)
  {
    var channel = CreateChannel(capacity);

    lock (_lock)
    {
      var wholeGopFits = _gopCache.Count <= capacity;
      foreach (var cached in _gopCache.Where(cached => wholeGopFits || cached.IsHeader))
        channel.Writer.TryWrite(cached);
      _subscribers.Add(new Entry(channel, demands));
      _snapshot = null;
    }
    Changed?.Invoke();

    return new ChannelDataStream<T>(Info, channel.Reader, () => Unsubscribe(channel));
  }

  private static Channel<T> CreateChannel(int capacity) =>
    Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = true,
      SingleWriter = false
    });

  private void Unsubscribe(Channel<T> channel)
  {
    lock (_lock)
    {
      _subscribers.RemoveAll(s => s.Channel == channel);
      _snapshot = null;
      if (!_subscribers.Any(s => s.Demands))
      {
        _gopCache.Clear();
        _cachingHeadersOnly = true;
      }
    }
    Changed?.Invoke();
  }

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    using var sub = Subscribe();
    await foreach (var item in sub.ReadAsync(ct))
      yield return item;
  }

  private readonly record struct Entry(Channel<T> Channel, bool Demands);

  void IDataStreamFanOut.Write(IDataUnit item) => Write((T)item);
  IDataStream IDataStreamFanOut.Subscribe(int capacity) => Subscribe(capacity);
  IDataStream IDataStreamFanOut.SubscribePassive(int capacity) => SubscribePassive(capacity);

  public ValueTask DisposeAsync()
  {
    lock (_lock)
    {
      foreach (var entry in _subscribers)
        entry.Channel.Writer.TryComplete();
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
    while (true)
    {
      bool available;
      try { available = await _reader.WaitToReadAsync(ct); }
      catch (OperationCanceledException) { yield break; }
      if (!available) break;
      while (_reader.TryRead(out var item))
        yield return item;
    }
  }

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 0)
      _onUnsubscribe();
  }
}
