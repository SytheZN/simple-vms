using System.Threading.Channels;
using Shared.Protocol;

namespace Client.Core.Tunnel;

public sealed class MuxStream : IAsyncDisposable
{
  private readonly StreamMuxer _muxer;
  private bool _disposed;

  public uint StreamId { get; }
  public ChannelReader<MuxMessage> Reader { get; }

  internal MuxStream(StreamMuxer muxer, uint streamId, ChannelReader<MuxMessage> reader)
  {
    _muxer = muxer;
    StreamId = streamId;
    Reader = reader;
  }

  public Task SendAsync(ushort flags, ReadOnlyMemory<byte> payload, CancellationToken ct) =>
    _muxer.SendAsync(StreamId, flags, payload, ct);

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    try
    {
      await _muxer.SendFinAsync(StreamId, CancellationToken.None);
    }
    catch (ObjectDisposedException) { }
    catch (IOException) { }

    _muxer.CloseStream(StreamId);
  }
}
