using System.Buffers.Binary;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Server.Tunnel;

internal sealed class StreamMuxer : IAsyncDisposable
{
  public delegate Task StreamHandler(
    ushort streamType,
    uint streamId,
    ChannelReader<MuxMessage> reader,
    CancellationToken ct);

  private readonly Stream _transport;
  private readonly SemaphoreSlim _writeLock = new(1, 1);
  private readonly Dictionary<uint, Channel<MuxMessage>> _streams = [];
  private readonly List<Task> _handlerTasks = [];
  private readonly ILogger _logger;
  private readonly Lock _lock = new();
  private bool _disposed;

  public StreamHandler? OnNewStream { get; set; }

  public StreamMuxer(Stream transport, ILogger logger)
  {
    _transport = transport;
    _logger = logger;
  }

  public ChannelReader<MuxMessage> GetOrCreateStream(uint streamId)
  {
    lock (_lock)
    {
      if (_streams.TryGetValue(streamId, out var existing))
        return existing.Reader;

      var channel = Channel.CreateBounded<MuxMessage>(new BoundedChannelOptions(256)
      {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = true
      });
      _streams[streamId] = channel;
      return channel.Reader;
    }
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

      Channel<MuxMessage>? channel;
      bool isNew;
      lock (_lock)
      {
        isNew = !_streams.ContainsKey(streamId);
        if (isNew)
        {
          var ch = Channel.CreateBounded<MuxMessage>(new BoundedChannelOptions(256)
          {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
          });
          _streams[streamId] = ch;
          channel = ch;
        }
        else
        {
          channel = _streams[streamId];
        }
      }

      if (channel == null)
        continue;

      if (isNew && OnNewStream != null)
      {
        ushort streamType = 0;
        if (payload.Length >= MessageEnvelope.StreamTypeHeaderSize)
          streamType = BinaryPrimitives.ReadUInt16LittleEndian(payload.Span);

        var remaining = payload.Length > MessageEnvelope.StreamTypeHeaderSize
          ? payload[MessageEnvelope.StreamTypeHeaderSize..]
          : ReadOnlyMemory<byte>.Empty;

        var reader = channel.Reader;
        var task = Task.Run(() => OnNewStream(streamType, streamId, reader, ct), ct);
        lock (_lock)
        {
          _handlerTasks.Add(task);
          if (_handlerTasks.Count % 64 == 0)
            _handlerTasks.RemoveAll(t => t.IsCompleted);
        }

        if (remaining.Length > 0)
          await channel.Writer.WriteAsync(new MuxMessage(typeFlags, remaining), ct);

        if (isFin)
        {
          channel.Writer.TryComplete();
          lock (_lock) _streams.Remove(streamId);
        }
        continue;
      }

      if (isErr || isFin)
      {
        if (payload.Length > 0)
          channel.Writer.TryWrite(new MuxMessage(typeFlags, payload));
        channel.Writer.TryComplete();
        lock (_lock) _streams.Remove(streamId);
        continue;
      }

      await channel.Writer.WriteAsync(new MuxMessage(typeFlags, payload), ct);
    }

    lock (_lock)
    {
      foreach (var (_, ch) in _streams)
        ch.Writer.TryComplete();
      _streams.Clear();
    }
  }

  public async Task SendAsync(
    uint streamId, ushort flags, ReadOnlyMemory<byte> payload, CancellationToken ct)
  {
    var header = new byte[MessageEnvelope.MuxHeaderSize];
    MessageEnvelope.WriteMuxHeader(header, streamId, flags, payload.Length);

    await _writeLock.WaitAsync(ct);
    try
    {
      await _transport.WriteAsync(header, ct);
      if (payload.Length > 0)
        await _transport.WriteAsync(payload, ct);
    }
    finally
    {
      _writeLock.Release();
    }
  }

  public Task SendFinAsync(uint streamId, CancellationToken ct) =>
    SendAsync(streamId, MessageEnvelope.FlagFin, ReadOnlyMemory<byte>.Empty, ct);

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    List<Task> tasks;
    lock (_lock)
    {
      foreach (var (_, ch) in _streams)
        ch.Writer.TryComplete();
      _streams.Clear();
      tasks = [.. _handlerTasks];
    }

    foreach (var task in tasks)
    {
      try { await task; }
      catch (OperationCanceledException) { }
      catch (Exception) { }
    }

    _writeLock.Dispose();
  }
}

internal readonly record struct MuxMessage(ushort Flags, ReadOnlyMemory<byte> Payload);
