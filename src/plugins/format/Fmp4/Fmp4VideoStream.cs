using Shared.Models;
using Shared.Models.Formats;

namespace Format.Fmp4;

public sealed class Fmp4VideoStream : IVideoStream<Fmp4Fragment>, IVideoStream
{
  private readonly Fmp4Muxer _muxer;

  public VideoStreamInfo Info { get; }
  public ReadOnlyMemory<byte> Header { get; }
  public Type FrameType => typeof(Fmp4Fragment);

  public Fmp4VideoStream(Fmp4Muxer muxer, VideoStreamInfo info)
  {
    _muxer = muxer;
    Info = info;
    Header = muxer.InitSegment ?? ReadOnlyMemory<byte>.Empty;
  }

  public IAsyncEnumerable<Fmp4Fragment> ReadAsync(CancellationToken ct) =>
    _muxer.MuxAsync(ct);
}
