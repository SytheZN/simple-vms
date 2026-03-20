using Shared.Models;
using Shared.Models.Formats;

namespace Format.Fmp4;

public sealed partial class Fmp4H264Plugin : IStreamFormat
{
  public string FormatId => "fmp4";
  public string FileExtension => ".mp4";
  public Type InputType => typeof(H264NalUnit);
  public Type OutputType => typeof(Fmp4Fragment);

  public OneOf<IVideoStream, Error> CreatePipeline(IDataStream input, StreamInfo info)
  {
    var timestamps = new TimestampConverter();
    var muxer = new Fmp4Muxer(MuxerCodec.H264, input, timestamps);
    return new Fmp4VideoStream(muxer, info);
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

  public OneOf<IVideoStream, Error> CreatePipeline(IDataStream input, StreamInfo info)
  {
    var timestamps = new TimestampConverter();
    var muxer = new Fmp4Muxer(MuxerCodec.H265, input, timestamps);
    return new Fmp4VideoStream(muxer, info);
  }

  public OneOf<ISegmentReader, Error> CreateReader(Stream input) =>
    new Fmp4SegmentReader(input);
}
