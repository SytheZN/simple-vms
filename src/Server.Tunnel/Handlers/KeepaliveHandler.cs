using System.Threading.Channels;
using MessagePack;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Server.Tunnel.Handlers;

internal static class KeepaliveHandler
{
  public static readonly TimeSpan DefaultSendInterval = TimeSpan.FromSeconds(15);
  public static readonly TimeSpan DefaultReceiveTimeout = TimeSpan.FromSeconds(10);

  public static Task RunAsync(
    ChannelReader<MuxMessage> reader,
    Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeFn,
    Action signalDead,
    ILogger logger,
    CancellationToken ct) =>
    RunAsync(reader, writeFn, signalDead, logger, DefaultSendInterval, DefaultReceiveTimeout, ct);

  public static async Task RunAsync(
    ChannelReader<MuxMessage> reader,
    Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeFn,
    Action signalDead,
    ILogger logger,
    TimeSpan sendInterval,
    TimeSpan receiveTimeout,
    CancellationToken ct)
  {
    var state = new KeepaliveState();

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    var readerTask = RunReaderAsync(reader, writeFn, timeoutCts.Token, state);
    var writerTask = RunWriterAsync(writeFn, sendInterval, timeoutCts.Token, state);

    var pollInterval = TimeSpan.FromMilliseconds(
      Math.Max(receiveTimeout.TotalMilliseconds / 10, 50));
    var timeoutTask = RunTimeoutAsync(receiveTimeout, pollInterval, timeoutCts.Token, state);

    var completed = await Task.WhenAny(readerTask, writerTask, timeoutTask);

    if (completed == timeoutTask && !ct.IsCancellationRequested)
    {
      logger.LogDebug("Keepalive timeout, signaling connection dead");
      signalDead();
    }

    timeoutCts.Cancel();

    try { await readerTask; } catch (OperationCanceledException) { }
    try { await writerTask; } catch (OperationCanceledException) { }
    try { await timeoutTask; } catch (OperationCanceledException) { }
  }

  private static async Task RunReaderAsync(
    ChannelReader<MuxMessage> reader,
    Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeFn,
    CancellationToken ct,
    KeepaliveState state)
  {
    while (!ct.IsCancellationRequested)
    {
      MuxMessage msg;
      try { msg = await reader.ReadAsync(ct); }
      catch (ChannelClosedException) { break; }

      var keepalive = MessagePackSerializer.Deserialize<KeepaliveMessage>(
        msg.Payload, ProtocolSerializer.Options);

      var wasOurs = state.RemovePendingEcho(keepalive.Echo);
      state.OnReceived();

      if (!wasOurs)
      {
        var response = MessagePackSerializer.Serialize(keepalive, ProtocolSerializer.Options);
        await writeFn(response, ct);
      }
    }
  }

  private static async Task RunWriterAsync(
    Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeFn,
    TimeSpan sendInterval,
    CancellationToken ct,
    KeepaliveState state)
  {
    while (!ct.IsCancellationRequested)
    {
      await Task.Delay(sendInterval, ct);

      if (state.IdleTime < sendInterval)
        continue;

      var echoValue = (ulong)Random.Shared.NextInt64();
      state.AddPendingEcho(echoValue);

      var msg = new KeepaliveMessage { Echo = echoValue };
      var payload = MessagePackSerializer.Serialize(msg, ProtocolSerializer.Options);
      await writeFn(payload, ct);
    }
  }

  private static async Task RunTimeoutAsync(
    TimeSpan receiveTimeout,
    TimeSpan pollInterval,
    CancellationToken ct,
    KeepaliveState state)
  {
    while (!ct.IsCancellationRequested)
    {
      await Task.Delay(pollInterval, ct);

      if (!state.HasPending)
        continue;

      if (state.PendingTime > receiveTimeout)
        return;
    }
  }

  private sealed class KeepaliveState
  {
    private readonly Lock _lock = new();
    private readonly HashSet<ulong> _pendingEchos = [];
    private DateTime _lastReceived = DateTime.UtcNow;
    private DateTime _firstPendingSent = DateTime.MaxValue;

    public TimeSpan IdleTime { get { lock (_lock) return DateTime.UtcNow - _lastReceived; } }
    public TimeSpan PendingTime { get { lock (_lock) return _pendingEchos.Count > 0 ? DateTime.UtcNow - _firstPendingSent : TimeSpan.Zero; } }
    public bool HasPending { get { lock (_lock) return _pendingEchos.Count > 0; } }

    public void OnReceived()
    {
      lock (_lock)
      {
        _lastReceived = DateTime.UtcNow;
        _pendingEchos.Clear();
        _firstPendingSent = DateTime.MaxValue;
      }
    }

    public void AddPendingEcho(ulong value)
    {
      lock (_lock)
      {
        if (_pendingEchos.Count == 0)
          _firstPendingSent = DateTime.UtcNow;
        _pendingEchos.Add(value);
      }
    }

    public bool RemovePendingEcho(ulong value)
    {
      lock (_lock) return _pendingEchos.Remove(value);
    }
  }
}
