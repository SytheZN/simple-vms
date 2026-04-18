using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Tunnel;

public sealed class MuxStream : IAsyncDisposable
{
  private readonly StreamMuxer _muxer;
  private readonly ILogger _logger;
  private bool _disposed;

  public uint StreamId { get; }
  public ChannelReader<MuxMessage> Reader { get; }

  internal MuxStream(StreamMuxer muxer, uint streamId, ChannelReader<MuxMessage> reader, ILogger logger)
  {
    _muxer = muxer;
    StreamId = streamId;
    Reader = reader;
    _logger = logger;
  }

  public Task SendAsync(ushort flags, ReadOnlyMemory<byte> payload, CancellationToken ct) =>
    _muxer.SendAsync(StreamId, flags, payload, ct);

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    _logger.LogDebug("MuxStream {StreamId}: sending FIN", StreamId);
    try
    {
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      await _muxer.SendFinAsync(StreamId, cts.Token);
      _logger.LogDebug("MuxStream {StreamId}: FIN sent", StreamId);
    }
    catch (ObjectDisposedException ex)
    {
      _logger.LogDebug(ex, "MuxStream {StreamId}: FIN skipped (transport disposed)", StreamId);
    }
    catch (IOException ex)
    {
      _logger.LogDebug(ex, "MuxStream {StreamId}: FIN failed (IO)", StreamId);
    }
    catch (OperationCanceledException ex)
    {
      _logger.LogDebug(ex, "MuxStream {StreamId}: FIN failed (cancelled/timeout)", StreamId);
    }

    _muxer.CloseStream(StreamId);
  }
}
