using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Server.Streaming;

public sealed class VideoStreamFanOut<T> : IVideoStream<T>, IAsyncDisposable where T : IDataUnit
{
  private readonly IVideoStream<T> _source;
  private readonly List<Subscriber> _subscribers = [];
  private readonly Lock _lock = new();
  private CancellationTokenSource? _loopCts;
  private Task? _readLoop;
  private bool _disposed;

  public VideoStreamInfo Info => _source.Info;
  public ReadOnlyMemory<byte> Header => _source.Header;
  public Type FrameType => typeof(T);
  public int SubscriberCount { get { lock (_lock) return _subscribers.Count; } }
  public Action? OnDemand { get; set; }
  public Action? OnEmpty { get; set; }
  public ILogger? Logger { get; set; }

  public VideoStreamFanOut(IVideoStream<T> source)
  {
    _source = source;
  }

  public IVideoStream<T> Subscribe(int capacity = 256)
  {
    var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = true,
      SingleWriter = false
    });

    var sub = new Subscriber(channel, waitingForKeyframe: true);

    Action? onDemand = null;
    lock (_lock)
    {
      _subscribers.Add(sub);
      if (_subscribers.Count == 1)
        onDemand = OnDemand;
    }

    if (onDemand != null)
    {
      StartReadLoop();
      onDemand.Invoke();
    }

    return new ChannelVideoStream<T>(Info, channel.Reader, () =>
    {
      Action? onEmpty = null;
      lock (_lock)
      {
        _subscribers.Remove(sub);
        if (_subscribers.Count == 0)
          onEmpty = OnEmpty;
      }
      if (onEmpty != null)
      {
        StopReadLoop();
        onEmpty.Invoke();
      }
    });
  }

  private void StartReadLoop()
  {
    lock (_lock)
    {
      if (_loopCts != null) return;
      _loopCts = new CancellationTokenSource();
    }

    var cts = _loopCts!;

    Logger?.LogDebug("VideoStreamFanOut<{Type}> starting read loop", typeof(T).Name);

    _readLoop = Task.Run(async () =>
    {
      long count = 0;
      try
      {
        await foreach (var item in _source.ReadAsync(cts.Token))
        {
          count++;
          if (count == 1)
            Logger?.LogDebug("VideoStreamFanOut<{Type}> received first item ({Bytes} bytes, sync={Sync})",
              typeof(T).Name, item.Data.Length, item.IsSyncPoint);
          else if (count % 500 == 0)
            Logger?.LogDebug("VideoStreamFanOut<{Type}> received {Count} items, subscribers={Subs}",
              typeof(T).Name, count, SubscriberCount);

          Subscriber[] snapshot;
          lock (_lock)
            snapshot = [.. _subscribers];

          foreach (var sub in snapshot)
          {
            if (sub.WaitingForKeyframe)
            {
              if (!item.IsSyncPoint)
                continue;
              sub.WaitingForKeyframe = false;
            }
            sub.Channel.Writer.TryWrite(item);
          }
        }

        Logger?.LogDebug("VideoStreamFanOut<{Type}> source completed after {Count} items",
          typeof(T).Name, count);
      }
      catch (OperationCanceledException)
      {
        Logger?.LogDebug("VideoStreamFanOut<{Type}> read loop stopped after {Count} items",
          typeof(T).Name, count);
      }
      catch (Exception ex)
      {
        Logger?.LogError(ex, "VideoStreamFanOut<{Type}> read loop failed after {Count} items",
          typeof(T).Name, count);
      }
    });
  }

  private void StopReadLoop()
  {
    CancellationTokenSource? cts;
    lock (_lock)
    {
      cts = _loopCts;
      _loopCts = null;
    }
    cts?.Cancel();
    cts?.Dispose();
  }

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    var sub = Subscribe();
    await foreach (var item in ((IVideoStream<T>)sub).ReadAsync(ct))
      yield return item;
  }

  private sealed class Subscriber(Channel<T> channel, bool waitingForKeyframe)
  {
    public Channel<T> Channel { get; } = channel;
    public bool WaitingForKeyframe { get; set; } = waitingForKeyframe;
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;
    StopReadLoop();
    if (_readLoop != null)
    {
      try { await _readLoop; }
      catch { }
    }
    lock (_lock)
    {
      foreach (var sub in _subscribers)
        sub.Channel.Writer.TryComplete();
      _subscribers.Clear();
    }
  }
}

internal sealed class ChannelVideoStream<T> : IVideoStream<T> where T : IDataUnit
{
  private readonly ChannelReader<T> _reader;
  private readonly Action _onDispose;

  public VideoStreamInfo Info { get; }
  public ReadOnlyMemory<byte> Header => ReadOnlyMemory<byte>.Empty;
  public Type FrameType => typeof(T);

  public ChannelVideoStream(VideoStreamInfo info, ChannelReader<T> reader, Action onDispose)
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
