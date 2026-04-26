using Shared.Models;
using Shared.Models.Formats;

namespace Format.Fmp4;

public sealed class Fmp4MuxStream : IMuxStream<Fmp4Fragment>
{
  private readonly Fmp4Muxer _muxer;

  public MuxStreamInfo Info { get; }
  public ReadOnlyMemory<byte> Header { get; }
  public Type FrameType => typeof(Fmp4Fragment);

  public Fmp4MuxStream(Fmp4Muxer muxer, MuxStreamInfo info)
  {
    _muxer = muxer;
    Info = info;
    Header = muxer.InitSegment ?? ReadOnlyMemory<byte>.Empty;
  }

  public IAsyncEnumerable<Fmp4Fragment> ReadAsync(CancellationToken ct) =>
    _muxer.MuxAsync(ct);
}
