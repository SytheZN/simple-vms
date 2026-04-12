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
  private CancellationTokenSource? _shutdownCts;
  private CancellationTokenSource? _sessionCts;
  private MuxStream? _stream;
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
    _logger.LogDebug("EventService starting");
    await _gate.WaitAsync(ct);
    try
    {
      await StopInternalAsync();
      _shutdownCts = new CancellationTokenSource();
      _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
      _stream = await _tunnel.OpenStreamAsync(
        StreamTypes.EventChannel, ReadOnlyMemory<byte>.Empty, ct);
      _running = true;
      _readLoop = ReadLoopAsync(_sessionCts.Token);
      _logger.LogDebug("EventService started");
    }
    finally { _gate.Release(); }
  }

  public async Task StopAsync(CancellationToken ct = default)
  {
    _logger.LogDebug("EventService stopping");
    _running = false;
    _shutdownCts?.Cancel();
    await _gate.WaitAsync(ct);
    try
    {
      await StopInternalAsync();
      _logger.LogDebug("EventService stopped");
    }
    finally { _gate.Release(); }
  }

  private async Task StopInternalAsync()
  {
    _logger.LogDebug("StopInternalAsync: cancelling session and disposing stream");
    _sessionCts?.Cancel();
    if (_stream != null)
    {
      await _stream.DisposeAsync();
      _stream = null;
    }
    if (_readLoop != null)
    {
      var loop = _readLoop;
      _readLoop = null;
      _ = loop.ContinueWith(_ => { }, TaskScheduler.Default);
    }
    _sessionCts?.Dispose();
    _sessionCts = null;
  }

  private async Task ReadLoopAsync(CancellationToken ct)
  {
    _logger.LogDebug("ReadLoopAsync started, token cancelled={Cancelled}", ct.IsCancellationRequested);
    try
    {
      while (!ct.IsCancellationRequested)
      {
        MuxMessage msg;
        try { msg = await _stream!.Reader.ReadAsync(ct); }
        catch (System.Threading.Channels.ChannelClosedException)
        {
          _logger.LogDebug("ReadLoopAsync: channel closed");
          break;
        }

        try
        {
          var eventMsg = MessagePackSerializer.Deserialize<EventChannelMessage>(
            msg.Payload, ProtocolSerializer.Options);
          var flags = (EventChannelFlags)msg.Flags;
          OnEvent?.Invoke(eventMsg, flags);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
          _logger.LogWarning(ex, "Failed to deserialize event message");
        }
      }
    }
    catch (OperationCanceledException)
    {
      _logger.LogDebug("ReadLoopAsync: cancelled");
    }
    _logger.LogDebug("ReadLoopAsync exited");
  }

  private void OnStateChanged(ConnectionState state)
  {
    if (state == ConnectionState.Connected && _running)
      _ = ResubscribeAsync();
  }

  private async Task ResubscribeAsync()
  {
    _logger.LogDebug("Resubscribing to event channel");
    try
    {
      await _gate.WaitAsync();
      try
      {
        if (!_running || _shutdownCts == null || _shutdownCts.IsCancellationRequested)
          return;
        await StopInternalAsync();
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        _stream = await _tunnel.OpenStreamAsync(
          StreamTypes.EventChannel, ReadOnlyMemory<byte>.Empty, _sessionCts.Token);
        _readLoop = ReadLoopAsync(_sessionCts.Token);
        _logger.LogDebug("Resubscribed to event channel");
      }
      finally { _gate.Release(); }
    }
    catch (OperationCanceledException)
    {
      _logger.LogDebug("Resubscribe cancelled");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to resubscribe to event channel after reconnect");
    }
  }

  public async ValueTask DisposeAsync()
  {
    _tunnel.StateChanged -= OnStateChanged;
    _running = false;
    _shutdownCts?.Cancel();
    await _gate.WaitAsync();
    try
    {
      await StopInternalAsync();
      _shutdownCts?.Dispose();
      _shutdownCts = null;
    }
    finally
    {
      _gate.Release();
      _gate.Dispose();
    }
  }
}
