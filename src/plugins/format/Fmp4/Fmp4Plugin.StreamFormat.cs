using Shared.Models;
using Shared.Models.Formats;

namespace Format.Fmp4;

public sealed partial class Fmp4H264Plugin : IStreamFormat
{
  public string FormatId => "fmp4";
  public string FileExtension => ".mp4";
  public Type InputType => typeof(H264NalUnit);
  public Type OutputType => typeof(Fmp4Fragment);

  public async Task<OneOf<IMuxStream, Error>> CreatePipelineAsync(
    IDataStream input, StreamInfo info, CancellationToken ct)
  {
    var muxer = new Fmp4Muxer(MuxerCodec.H264, input);
    var outputInfo = await muxer.InitAsync((int)Math.Round(info.Fps ?? 0m), ct);
    return new Fmp4MuxStream(muxer, outputInfo);
  }

  public OneOf<ISegmentReader, Error> CreateReader(Stream input) =>
    new Fmp4SegmentReader(input);
}

public sealed partial class Fmp4H265Plugin : IStreamFormat
{
  public string FormatId => "fmp4";
  public string FileExtension => ".mp4";
  public Type InputType => typeof(H265NalUnit);
  public Type OutputType => typeof(Fmp4Fragment);

  public async Task<OneOf<IMuxStream, Error>> CreatePipelineAsync(
    IDataStream input, StreamInfo info, CancellationToken ct)
  {
    var muxer = new Fmp4Muxer(MuxerCodec.H265, input);
    var outputInfo = await muxer.InitAsync((int)Math.Round(info.Fps ?? 0m), ct);
    return new Fmp4MuxStream(muxer, outputInfo);
  }

  public OneOf<ISegmentReader, Error> CreateReader(Stream input) =>
    new Fmp4SegmentReader(input);
}
