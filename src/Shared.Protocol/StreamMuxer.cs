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

  // Bounds bytes in flight per stream. A stream abandoned mid-transfer can only strand
  // this much data on the shared transport, which is what the peer must drain before
  // anything else gets through.
  public const int StreamWindowBytes = 1024 * 1024;

  private sealed class StreamEntry
  {
    public required Channel<MuxMessage> Channel { get; init; }
    public required CancellationTokenSource Cts { get; init; }
    public SemaphoreSlim CreditChanged { get; } = new(0);
    public long SendCredit { get; set; } = StreamWindowBytes;
    public long PendingAck { get; set; }
  }
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
    return new StreamEntry { Channel = channel, Cts = cts };
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
    entry.CreditChanged.Release();
  }

  public async Task RunReadLoopAsync(CancellationToken ct)
  {
    var header = new byte[MessageEnvelope.MuxHeaderSize];

    while (!ct.IsCancellationRequested)
    {
      int read;
      try
      {
        read = await _transport.ReadAtLeastAsync(header, MessageEnvelope.MuxHeaderSize, false, ct)
          .ConfigureAwait(false);
      }
      catch (EndOfStreamException)
      {
        _logger.LogDebug("StreamMuxer: read loop exit - peer closed transport (header)");
        break;
      }
      catch (OperationCanceledException)
      {
        _logger.LogDebug("StreamMuxer: read loop exit - cancelled (header)");
        break;
      }
      catch (IOException ex)
      {
        _logger.LogDebug(ex, "StreamMuxer: read loop exit - IO error (header)");
        break;
      }

      if (read < MessageEnvelope.MuxHeaderSize)
      {
        _logger.LogDebug("StreamMuxer: read loop exit - short header read {Read}/{Expected}",
          read, MessageEnvelope.MuxHeaderSize);
        break;
      }

      var (streamId, flags, payloadLength) = MessageEnvelope.ReadMuxHeader(header);

      ReadOnlyMemory<byte> payload = ReadOnlyMemory<byte>.Empty;
      if (payloadLength > 0)
      {
        var buf = new byte[payloadLength];
        try
        {
          await _transport.ReadExactlyAsync(buf, ct).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
          _logger.LogDebug(
            "StreamMuxer: read loop exit - peer closed transport mid-payload"
            + " (stream {StreamId}, {Length} bytes expected)", streamId, payloadLength);
          break;
        }
        catch (OperationCanceledException)
        {
          _logger.LogDebug("StreamMuxer: read loop exit - cancelled mid-payload (stream {StreamId})",
            streamId);
          break;
        }
        catch (IOException ex)
        {
          _logger.LogDebug(ex, "StreamMuxer: read loop exit - IO error mid-payload (stream {StreamId})",
            streamId);
          break;
        }
        payload = buf;
      }

      var isFin = (flags & MessageEnvelope.FlagFin) != 0;
      var isErr = (flags & MessageEnvelope.FlagErr) != 0;
      var typeFlags = (ushort)(flags & MessageEnvelope.TypeFlagMask);

      if ((flags & MessageEnvelope.FlagWindowUpdate) != 0)
      {
        if (payload.Length >= MessageEnvelope.WindowUpdateSize)
          GrantCredit(streamId, MessageEnvelope.ReadWindowUpdate(payload.Span));
        continue;
      }

      if (payload.Length > 0)
      {
        // Credits are additive, so delivery order does not matter. Sending inline would
        // park the read loop on the write lock behind whatever large frame is going out.
        var ack = RecordReceived(streamId, payload.Length);
        if (ack > 0)
          _ = SendWindowUpdateAsync(streamId, ack, ct);
      }

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
          await WriteWithBacklogLogAsync(entry.Channel, streamId, new MuxMessage(typeFlags, remaining), ct)
            .ConfigureAwait(false);

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

      await WriteWithBacklogLogAsync(entry.Channel, streamId, new MuxMessage(typeFlags, payload), ct)
        .ConfigureAwait(false);
    }

    if (ct.IsCancellationRequested)
      _logger.LogDebug("StreamMuxer: read loop exit - connection token cancelled");

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
    await channel.Writer.WriteAsync(msg, ct).ConfigureAwait(false);
  }

  public async Task SendAsync(
    uint streamId, ushort flags, ReadOnlyMemory<byte> payload, CancellationToken ct)
  {
    if ((flags & MessageEnvelope.ControlFlagMask) == 0 && payload.Length > 0)
      await AcquireCreditAsync(streamId, payload.Length, ct).ConfigureAwait(false);

    var total = MessageEnvelope.MuxHeaderSize + payload.Length;
    var frame = ArrayPool<byte>.Shared.Rent(total);
    try
    {
      MessageEnvelope.WriteMuxHeader(frame, streamId, flags, payload.Length);
      if (payload.Length > 0)
        payload.Span.CopyTo(frame.AsSpan(MessageEnvelope.MuxHeaderSize));

      // Single write so header and payload land in the same TLS record
      // (frames above the TLS plaintext max will still fan out).
      await _writeLock.WaitAsync(ct).ConfigureAwait(false);
      try
      {
        // Cancellation is honoured up to the moment the frame starts going out, never
        // during it: aborting mid-frame leaves a partial record on the shared transport
        // and desyncs the peer's decrypt for every record after it.
        await _transport.WriteAsync(frame.AsMemory(0, total), CancellationToken.None)
          .ConfigureAwait(false);
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

  private async Task AcquireCreditAsync(uint streamId, int bytes, CancellationToken ct)
  {
    while (true)
    {
      StreamEntry? entry;
      lock (_lock)
      {
        if (!_streams.TryGetValue(streamId, out entry))
          return;

        // A frame at or above the window can never be covered outright once anything at all
        // is outstanding, and credit only returns once the peer has acked half a window - so
        // it goes out on whatever credit exists. Credit may go negative; acks restore it.
        var need = Math.Min(bytes, StreamWindowBytes);
        if (entry.SendCredit >= need || (bytes >= StreamWindowBytes && entry.SendCredit > 0))
        {
          entry.SendCredit -= bytes;
          return;
        }
      }

      await entry.CreditChanged.WaitAsync(ct).ConfigureAwait(false);
    }
  }

  private void GrantCredit(uint streamId, int bytes)
  {
    StreamEntry? entry;
    lock (_lock)
    {
      if (!_streams.TryGetValue(streamId, out entry))
        return;
      entry.SendCredit += bytes;
    }
    entry.CreditChanged.Release();
  }

  private int RecordReceived(uint streamId, int bytes)
  {
    lock (_lock)
    {
      if (!_streams.TryGetValue(streamId, out var entry))
        return 0;

      entry.PendingAck += bytes;
      if (entry.PendingAck < StreamWindowBytes / 2)
        return 0;

      var ack = (int)entry.PendingAck;
      entry.PendingAck = 0;
      return ack;
    }
  }

  private async Task SendWindowUpdateAsync(uint streamId, int bytes, CancellationToken ct)
  {
    var payload = new byte[MessageEnvelope.WindowUpdateSize];
    MessageEnvelope.WriteWindowUpdate(payload, bytes);
    try
    {
      await SendAsync(streamId, MessageEnvelope.FlagWindowUpdate, payload, ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
    {
      _logger.LogDebug("StreamMuxer: window update for stream {StreamId} not sent", streamId);
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
      try { await task.ConfigureAwait(false); }
      catch (OperationCanceledException) { }
      catch (Exception) { }
    }

    _writeLock.Dispose();
  }
}

public readonly record struct MuxMessage(ushort Flags, ReadOnlyMemory<byte> Payload);
