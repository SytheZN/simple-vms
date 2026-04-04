using Server.Streaming;
using Shared.Protocol;

namespace Server.Tunnel;

internal sealed class TunnelStreamSink : IStreamSink
{
  private readonly StreamMuxer _muxer;
  private readonly uint _streamId;
  private volatile bool _open = true;

  public TunnelStreamSink(StreamMuxer muxer, uint streamId)
  {
    _muxer = muxer;
    _streamId = streamId;
  }

  public bool IsOpen => _open;

  public Task SendInitAsync(string profile, ReadOnlyMemory<byte> data, CancellationToken ct) =>
    SendAsync(StreamMessageWriter.SerializeInit(profile, data), ct);

  public Task SendGopAsync(GopFlags flags, string profile, ulong timestamp, ReadOnlyMemory<byte> data, CancellationToken ct) =>
    SendAsync(StreamMessageWriter.SerializeGop(flags, profile, timestamp, data), ct);

  public Task SendStatusAsync(StreamStatus status, CancellationToken ct) =>
    SendAsync(StreamMessageWriter.SerializeStatus(status), ct);

  public Task SendGapAsync(ulong from, ulong to, CancellationToken ct) =>
    SendAsync(StreamMessageWriter.SerializeGap(from, to), ct);

  public void Close() => _open = false;

  private async Task SendAsync(byte[] data, CancellationToken ct)
  {
    if (!_open) return;
    try
    {
      await _muxer.SendAsync(_streamId, 0, data, ct);
    }
    catch (IOException)
    {
      _open = false;
    }
    catch (ObjectDisposedException)
    {
      _open = false;
    }
  }
}
