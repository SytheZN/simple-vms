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
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      await _muxer.SendFinAsync(StreamId, cts.Token);
    }
    catch (ObjectDisposedException) { }
    catch (IOException) { }
    catch (OperationCanceledException) { }

    _muxer.CloseStream(StreamId);
  }
}
