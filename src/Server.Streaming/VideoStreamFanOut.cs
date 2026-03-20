using System.Threading.Channels;
using Shared.Models;

namespace Server.Streaming;

public sealed class VideoStreamFanOut<T> : IVideoStream<T>, IAsyncDisposable where T : IDataUnit
{
  private readonly IVideoStream<T> _source;
  private readonly List<Channel<T>> _subscribers = [];
  private readonly Lock _lock = new();
  private readonly CancellationTokenSource _cts = new();
  private Task? _readLoop;

  public StreamInfo Info => _source.Info;
  public Type FrameType => typeof(T);
  public int SubscriberCount { get { lock (_lock) return _subscribers.Count; } }
  public Action? OnEmpty { get; set; }

  public VideoStreamFanOut(IVideoStream<T> source)
  {
    _source = source;
  }

  public void Start()
  {
    _readLoop = Task.Run(async () =>
    {
      try
      {
        await foreach (var item in _source.ReadAsync(_cts.Token))
        {
          Channel<T>[] snapshot;
          lock (_lock)
            snapshot = [.. _subscribers];

          foreach (var channel in snapshot)
            channel.Writer.TryWrite(item);
        }
      }
      catch (OperationCanceledException) { }
      finally
      {
        lock (_lock)
        {
          foreach (var channel in _subscribers)
            channel.Writer.TryComplete();
        }
      }
    });
  }

  public IVideoStream<T> Subscribe(int capacity = 256)
  {
    var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = true,
      SingleWriter = true
    });

    lock (_lock)
      _subscribers.Add(channel);

    return new ChannelVideoStream<T>(Info, channel.Reader, () =>
    {
      Action? onEmpty = null;
      lock (_lock)
      {
        _subscribers.Remove(channel);
        if (_subscribers.Count == 0)
          onEmpty = OnEmpty;
      }
      onEmpty?.Invoke();
    });
  }

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    var sub = Subscribe();
    await foreach (var item in ((IVideoStream<T>)sub).ReadAsync(ct))
      yield return item;
  }

  public async ValueTask DisposeAsync()
  {
    _cts.Cancel();
    if (_readLoop != null)
    {
      try { await _readLoop; }
      catch { /* swallow */ }
    }
    _cts.Dispose();
  }
}

internal sealed class ChannelVideoStream<T> : IVideoStream<T> where T : IDataUnit
{
  private readonly ChannelReader<T> _reader;
  private readonly Action _onDispose;

  public StreamInfo Info { get; }
  public Type FrameType => typeof(T);

  public ChannelVideoStream(StreamInfo info, ChannelReader<T> reader, Action onDispose)
  {
    Info = info;
    _reader = reader;
    _onDispose = onDispose;
  }

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    try
    {
      await foreach (var item in _reader.ReadAllAsync(ct))
        yield return item;
    }
    finally
    {
      _onDispose();
    }
  }
}
