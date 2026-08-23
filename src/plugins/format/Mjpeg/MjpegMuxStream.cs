using Shared.Models;
using Shared.Models.Formats;

namespace Format.Mjpeg;

public sealed class MjpegMuxStream : IMuxStream<JpegFragment>
{
  private readonly MjpegMuxer _muxer;

  public MuxStreamInfo Info { get; }
  public ReadOnlyMemory<byte> Header => ReadOnlyMemory<byte>.Empty;
  public Type FrameType => typeof(JpegFragment);

  public MjpegMuxStream(MjpegMuxer muxer, MuxStreamInfo info)
  {
    _muxer = muxer;
    Info = info;
  }

  public IAsyncEnumerable<JpegFragment> ReadAsync(CancellationToken ct) =>
    _muxer.MuxAsync(ct);
}
