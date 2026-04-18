using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Shared.Protocol;

public sealed class StreamMuxer : IAsyncDisposable
{
  public delegate Task StreamHandler(
    ushort streamType,
    uint streamId,
    ChannelReader<MuxMessage> reader,
    CancellationToken ct);

  private readonly Stream _transport;
  private readonly SemaphoreSlim _writeLock = new(1, 1);
  private readonly Dictionary<uint, StreamEntry> _streams = [];
  private readonly List<Task> _handlerTasks = [];

  private sealed record StreamEntry(Channel<MuxMessage> Channel, CancellationTokenSource Cts);
  private readonly ILogger _logger;
  private readonly Lock _lock = new();
  private readonly uint _startStreamId;
  private uint _nextStreamId;
  private bool _disposed;

  public StreamHandler? OnNewStream { get; set; }

  public StreamMuxer(Stream transport, ILogger logger, uint startStreamId = 0)
  {
    _transport = transport;
    _logger = logger;
    _startStreamId = startStreamId;
    _nextStreamId = startStreamId;
  }

  public (uint StreamId, ChannelReader<MuxMessage> Reader) OpenStream(
    ushort streamType, ReadOnlyMemory<byte> initialPayload = default)
  {
    lock (_lock)
    {
      var streamId = _nextStreamId;
      _nextStreamId += 2;

      var entry = NewEntry();
      _streams[streamId] = entry;

      var typeHeader = new byte[MessageEnvelope.StreamTypeHeaderSize];
      MessageEnvelope.WriteStreamType(typeHeader, streamType);

      var payload = new byte[typeHeader.Length + initialPayload.Length];
      typeHeader.CopyTo(payload.AsSpan());
      initialPayload.Span.CopyTo(payload.AsSpan(typeHeader.Length));

      _ = SendAsync(streamId, 0, payload, CancellationToken.None);

      return (streamId, entry.Channel.Reader);
    }
  }

  public ChannelReader<MuxMessage> GetOrCreateStream(uint streamId)
  {
    lock (_lock)
    {
      if (_streams.TryGetValue(streamId, out var existing))
        return existing.Channel.Reader;

      var entry = NewEntry();
      _streams[streamId] = entry;
      return entry.Channel.Reader;
    }
  }

  private static StreamEntry NewEntry(CancellationToken linkTo = default)
  {
    var channel = Channel.CreateBounded<MuxMessage>(new BoundedChannelOptions(256)
    {
      FullMode = BoundedChannelFullMode.Wait,
      SingleReader = true,
      SingleWriter = true
    });
    var cts = CancellationTokenSource.CreateLinkedTokenSource(linkTo);
    return new StreamEntry(channel, cts);
  }

  private void CompleteAndRemove(uint streamId)
  {
    StreamEntry? entry;
    lock (_lock)
    {
      if (!_streams.Remove(streamId, out entry)) return;
    }
    entry.Channel.Writer.TryComplete();
    try { entry.Cts.Cancel(); } catch (ObjectDisposedException) { }
    entry.Cts.Dispose();
  }

  public async Task RunReadLoopAsync(CancellationToken ct)
  {
    var header = new byte[MessageEnvelope.MuxHeaderSize];

    while (!ct.IsCancellationRequested)
    {
      int read;
      try
      {
        read = await _transport.ReadAtLeastAsync(header, MessageEnvelope.MuxHeaderSize, false, ct);
      }
      catch (EndOfStreamException) { break; }
      catch (OperationCanceledException) { break; }
      catch (IOException) { break; }

      if (read < MessageEnvelope.MuxHeaderSize)
        break;

      var (streamId, flags, payloadLength) = MessageEnvelope.ReadMuxHeader(header);

      ReadOnlyMemory<byte> payload = ReadOnlyMemory<byte>.Empty;
      if (payloadLength > 0)
      {
        var buf = new byte[payloadLength];
        try
        {
          await _transport.ReadExactlyAsync(buf, ct);
        }
        catch (EndOfStreamException) { break; }
        catch (OperationCanceledException) { break; }
        catch (IOException) { break; }
        payload = buf;
      }

      var isFin = (flags & MessageEnvelope.FlagFin) != 0;
      var isErr = (flags & MessageEnvelope.FlagErr) != 0;
      var typeFlags = (ushort)(flags & MessageEnvelope.TypeFlagMask);

      StreamEntry? entry;
      bool isNew;
      lock (_lock)
      {
        isNew = !_streams.ContainsKey(streamId);
        if (isNew)
        {
          if (OnNewStream == null)
          {
            _logger.LogDebug("StreamMuxer: dropping message for unknown stream {StreamId} (no handler)",
              streamId);
            continue;
          }
          entry = NewEntry(ct);
          _streams[streamId] = entry;
        }
        else
        {
          entry = _streams[streamId];
        }
      }

      if (entry == null)
        continue;

      if (isNew && OnNewStream != null)
      {
        ushort streamType = 0;
        if (payload.Length >= MessageEnvelope.StreamTypeHeaderSize)
          streamType = BinaryPrimitives.ReadUInt16LittleEndian(payload.Span);

        var remaining = payload.Length > MessageEnvelope.StreamTypeHeaderSize
          ? payload[MessageEnvelope.StreamTypeHeaderSize..]
          : ReadOnlyMemory<byte>.Empty;

        var reader = entry.Channel.Reader;
        var streamCt = entry.Cts.Token;
        var task = Task.Run(() => OnNewStream(streamType, streamId, reader, streamCt), ct);
        lock (_lock)
        {
          _handlerTasks.Add(task);
          if (_handlerTasks.Count % 64 == 0)
            _handlerTasks.RemoveAll(t => t.IsCompleted);
        }

        if (remaining.Length > 0)
          await WriteWithBacklogLogAsync(entry.Channel, streamId, new MuxMessage(typeFlags, remaining), ct);

        if (isFin)
          CompleteAndRemove(streamId);
        continue;
      }

      if (isErr || isFin)
      {
        if (payload.Length > 0)
          entry.Channel.Writer.TryWrite(new MuxMessage(typeFlags, payload));
        CompleteAndRemove(streamId);
        continue;
      }

      await WriteWithBacklogLogAsync(entry.Channel, streamId, new MuxMessage(typeFlags, payload), ct);
    }

    List<uint> toClose;
    lock (_lock) toClose = [.. _streams.Keys];
    foreach (var id in toClose) CompleteAndRemove(id);
  }

  private async Task WriteWithBacklogLogAsync(
    Channel<MuxMessage> channel, uint streamId, MuxMessage msg, CancellationToken ct)
  {
    if (channel.Writer.TryWrite(msg)) return;
    _logger.LogDebug("StreamMuxer: stream {StreamId} channel full ({Count}/256), mux read loop blocked",
      streamId, channel.Reader.Count);
    await channel.Writer.WriteAsync(msg, ct);
  }

  public async Task SendAsync(
    uint streamId, ushort flags, ReadOnlyMemory<byte> payload, CancellationToken ct)
  {
    var total = MessageEnvelope.MuxHeaderSize + payload.Length;
    var frame = ArrayPool<byte>.Shared.Rent(total);
    try
    {
      MessageEnvelope.WriteMuxHeader(frame, streamId, flags, payload.Length);
      if (payload.Length > 0)
        payload.Span.CopyTo(frame.AsSpan(MessageEnvelope.MuxHeaderSize));

      // Single write so header and payload land in the same TLS record
      // (frames above the TLS plaintext max will still fan out).
      await _writeLock.WaitAsync(ct);
      try
      {
        await _transport.WriteAsync(frame.AsMemory(0, total), ct);
      }
      finally
      {
        _writeLock.Release();
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(frame);
    }
  }

  public Task SendFinAsync(uint streamId, CancellationToken ct) =>
    SendAsync(streamId, MessageEnvelope.FlagFin, ReadOnlyMemory<byte>.Empty, ct);

  public void CloseStream(uint streamId) => CompleteAndRemove(streamId);

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    List<uint> ids;
    List<Task> tasks;
    lock (_lock)
    {
      ids = [.. _streams.Keys];
      tasks = [.. _handlerTasks];
    }
    foreach (var id in ids) CompleteAndRemove(id);

    foreach (var task in tasks)
    {
      try { await task; }
      catch (OperationCanceledException) { }
      catch (Exception) { }
    }

    _writeLock.Dispose();
  }
}

public readonly record struct MuxMessage(ushort Flags, ReadOnlyMemory<byte> Payload);
