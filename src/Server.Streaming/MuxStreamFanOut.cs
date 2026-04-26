using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Server.Streaming;

public sealed class MuxStreamFanOut<T> : IMuxStream<T>, IMuxStreamFanOut where T : IDataUnit
{
  private readonly IMuxStream<T> _source;
  private readonly List<Subscriber> _subscribers = [];
  private readonly Lock _lock = new();
  private Subscriber[]? _snapshot;
  private CancellationTokenSource? _loopCts;
  private Task? _readLoop;
  private bool _disposed;
  private List<T> _currentGop = [];

  public MuxStreamInfo Info => _source.Info;
  public ReadOnlyMemory<byte> Header => _source.Header;
  public Type FrameType => typeof(T);
  public int SubscriberCount { get { lock (_lock) return _subscribers.Count; } }
  public Action? OnDemand { get; set; }
  public Action? OnEmpty { get; set; }
  public ILogger? Logger { get; set; }

  public MuxStreamFanOut(IMuxStream<T> source)
  {
    _source = source;
  }

  public IMuxStream<T> Subscribe(int capacity = 256)
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
      if (_currentGop.Count > 0)
      {
        foreach (var frame in _currentGop)
          channel.Writer.TryWrite(frame);
        sub.WaitingForKeyframe = false;
      }
      _subscribers.Add(sub);
      _snapshot = null;
      if (_subscribers.Count == 1)
        onDemand = OnDemand;
    }

    if (onDemand != null)
    {
      StartReadLoop();
      onDemand.Invoke();
    }

    return new ChannelMuxStream<T>(Info, channel.Reader, () =>
    {
      Action? onEmpty = null;
      lock (_lock)
      {
        _subscribers.Remove(sub);
        _snapshot = null;
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
    CancellationTokenSource cts;
    lock (_lock)
    {
      if (_disposed) return;
      if (_loopCts != null) return;
      cts = _loopCts = new CancellationTokenSource();
    }

    Logger?.LogDebug("MuxStreamFanOut<{Type}> starting read loop", typeof(T).Name);

    _readLoop = Task.Run(async () =>
    {
      long count = 0;
      try
      {
        await foreach (var item in _source.ReadAsync(cts.Token))
        {
          count++;
          if (count == 1)
            Logger?.LogDebug("MuxStreamFanOut<{Type}> received first item ({Bytes} bytes, sync={Sync})",
              typeof(T).Name, item.Data.Length, item.IsSyncPoint);
          else if (count % 500 == 0)
            Logger?.LogDebug("MuxStreamFanOut<{Type}> received {Count} items, subscribers={Subs}",
              typeof(T).Name, count, SubscriberCount);

          Subscriber[] snapshot;
          lock (_lock)
          {
            if (item.IsSyncPoint)
              _currentGop = [item];
            else
              _currentGop.Add(item);

            snapshot = _snapshot ??= [.. _subscribers];
          }

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

        Logger?.LogDebug("MuxStreamFanOut<{Type}> source completed after {Count} items",
          typeof(T).Name, count);
      }
      catch (OperationCanceledException)
      {
        Logger?.LogDebug("MuxStreamFanOut<{Type}> read loop stopped after {Count} items",
          typeof(T).Name, count);
      }
      catch (Exception ex)
      {
        Logger?.LogError(ex, "MuxStreamFanOut<{Type}> read loop failed after {Count} items",
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
    lock (_lock)
      _currentGop = [];
  }

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    var sub = Subscribe();
    await foreach (var item in ((IMuxStream<T>)sub).ReadAsync(ct))
      yield return item;
  }

  private sealed class Subscriber(Channel<T> channel, bool waitingForKeyframe)
  {
    public Channel<T> Channel { get; } = channel;
    public bool WaitingForKeyframe { get; set; } = waitingForKeyframe;
  }

  IMuxStream IMuxStreamFanOut.Subscribe(int capacity) => Subscribe(capacity);

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

internal sealed class ChannelMuxStream<T> : IMuxStream<T> where T : IDataUnit
{
  private readonly ChannelReader<T> _reader;
  private readonly Action _onDispose;

  public MuxStreamInfo Info { get; }
  public ReadOnlyMemory<byte> Header => ReadOnlyMemory<byte>.Empty;
  public Type FrameType => typeof(T);

  public ChannelMuxStream(MuxStreamInfo info, ChannelReader<T> reader, Action onDispose)
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
      while (await _reader.WaitToReadAsync(ct))
      {
        while (_reader.TryRead(out var item))
          yield return item;
      }
    }
    finally
    {
      _onDispose();
    }
  }
}
