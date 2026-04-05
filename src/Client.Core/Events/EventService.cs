using Client.Core.Tunnel;
using MessagePack;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Events;

public sealed class EventService : IEventService, IAsyncDisposable
{
  private readonly ITunnelService _tunnel;
  private readonly ILogger<EventService> _logger;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private MuxStream? _stream;
  private CancellationTokenSource? _cts;
  private Task? _readLoop;
  private volatile bool _running;

  public event Action<EventChannelMessage, EventChannelFlags>? OnEvent;

  public EventService(ITunnelService tunnel, ILogger<EventService> logger)
  {
    _tunnel = tunnel;
    _logger = logger;
    _tunnel.StateChanged += OnStateChanged;
  }

  public async Task StartAsync(CancellationToken ct)
  {
    await _gate.WaitAsync(ct);
    try
    {
      await StopInternalAsync();
      _cts = new CancellationTokenSource();
      _stream = await _tunnel.OpenStreamAsync(
        StreamTypes.EventChannel, ReadOnlyMemory<byte>.Empty, ct);
      _running = true;
      _readLoop = ReadLoopAsync(_cts.Token);
    }
    finally { _gate.Release(); }
  }

  public async Task StopAsync()
  {
    await _gate.WaitAsync();
    try
    {
      _running = false;
      await StopInternalAsync();
    }
    finally { _gate.Release(); }
  }

  private async Task StopInternalAsync()
  {
    _cts?.Cancel();
    if (_readLoop != null)
    {
      try { await _readLoop; }
      catch (OperationCanceledException) { }
    }
    if (_stream != null)
      await _stream.DisposeAsync();
    _cts?.Dispose();
    _stream = null;
    _cts = null;
    _readLoop = null;
  }

  private async Task ReadLoopAsync(CancellationToken ct)
  {
    try
    {
      while (!ct.IsCancellationRequested)
      {
        MuxMessage msg;
        try { msg = await _stream!.Reader.ReadAsync(ct); }
        catch (System.Threading.Channels.ChannelClosedException) { break; }

        try
        {
          var eventMsg = MessagePackSerializer.Deserialize<EventChannelMessage>(
            msg.Payload, ProtocolSerializer.Options);
          var flags = (EventChannelFlags)msg.Flags;
          OnEvent?.Invoke(eventMsg, flags);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
          // Malformed message - skip and continue
        }
      }
    }
    catch (OperationCanceledException) { }
  }

  private void OnStateChanged(ConnectionState state)
  {
    if (state == ConnectionState.Connected && _running)
      _ = ResubscribeAsync();
  }

  private async Task ResubscribeAsync()
  {
    try
    {
      await _gate.WaitAsync();
      try
      {
        if (!_running) return;
        await StopInternalAsync();
        _cts = new CancellationTokenSource();
        _stream = await _tunnel.OpenStreamAsync(
          StreamTypes.EventChannel, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        _readLoop = ReadLoopAsync(_cts.Token);
      }
      finally { _gate.Release(); }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Failed to resubscribe to event channel after reconnect");
    }
  }

  public async ValueTask DisposeAsync()
  {
    _tunnel.StateChanged -= OnStateChanged;
    _running = false;
    await _gate.WaitAsync();
    try { await StopInternalAsync(); }
    finally
    {
      _gate.Release();
      _gate.Dispose();
    }
  }
}
