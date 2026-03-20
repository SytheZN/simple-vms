using Shared.Models;
using Shared.Models.Formats;

namespace Format.Fmp4;

public sealed class Fmp4VideoStream : IVideoStream<Fmp4Fragment>, IVideoStream
{
  private readonly Fmp4Muxer _muxer;
  private readonly StreamInfo _info;

  public Fmp4VideoStream(Fmp4Muxer muxer, StreamInfo inputInfo)
  {
    _muxer = muxer;
    _info = new StreamInfo
    {
      DataFormat = "fmp4",
      FormatParameters = null,
      Resolution = inputInfo.Resolution,
      Fps = inputInfo.Fps
    };
  }

  public StreamInfo Info => _info;
  public Type FrameType => typeof(Fmp4Fragment);

  public IAsyncEnumerable<Fmp4Fragment> ReadAsync(CancellationToken ct) =>
    _muxer.MuxAsync(ct);
}
