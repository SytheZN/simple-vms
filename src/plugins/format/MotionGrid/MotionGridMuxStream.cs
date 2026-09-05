using Shared.Models;
using Shared.Models.Formats;

namespace Format.MotionGrid;

public sealed class MotionGridMuxStream : IMuxStream<MotionGridFragment>
{
  private readonly MotionGridMuxer _muxer;

  public MuxStreamInfo Info { get; }
  public ReadOnlyMemory<byte> Header => ReadOnlyMemory<byte>.Empty;
  public Type FrameType => typeof(MotionGridFragment);
  public Action<MuxStreamStats>? OnStats { set => _muxer.OnStats = value; }

  public MotionGridMuxStream(MotionGridMuxer muxer, MuxStreamInfo info)
  {
    _muxer = muxer;
    Info = info;
  }

  public IAsyncEnumerable<MotionGridFragment> ReadAsync(CancellationToken ct) =>
    _muxer.MuxAsync(ct);
}
