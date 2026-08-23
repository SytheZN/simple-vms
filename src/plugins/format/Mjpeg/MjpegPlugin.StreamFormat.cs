using Shared.Models;
using Shared.Models.Formats;

namespace Format.Mjpeg;

public sealed partial class MjpegPlugin : IStreamFormat
{
  public string FormatId => "mjpeg";
  public string FileExtension => "mjpg";
  public Type InputType => typeof(JpegUnit);
  public Type OutputType => typeof(JpegFragment);

  public Task<OneOf<IMuxStream, Error>> CreatePipelineAsync(
    IDataStream input, StreamInfo info, CancellationToken ct)
  {
    var muxer = new MjpegMuxer((IDataStream<JpegUnit>)input, FileExtension);
    return Task.FromResult<OneOf<IMuxStream, Error>>(
      new MjpegMuxStream(muxer, muxer.Init()));
  }

  public OneOf<ISegmentReader, Error> CreateReader(Stream input) =>
    Error.Create(ModuleIds.PluginMjpeg, 0x0001, Result.Unavailable,
      "MJPEG playback is not implemented");
}
