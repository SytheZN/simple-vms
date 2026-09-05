using Shared.Models;
using Shared.Models.Formats;

namespace Format.MotionGrid;

public sealed partial class MotionGridPlugin : IStreamFormat
{
  public string FormatId => "motion-grid";
  public string FileExtension => "mgrd";
  public Type InputType => typeof(MotionGridUnit);
  public Type OutputType => typeof(MotionGridFragment);

  public async Task<OneOf<IMuxStream, Error>> CreatePipelineAsync(
    IDataStream input, StreamInfo info, CancellationToken ct)
  {
    var muxer = new MotionGridMuxer((IDataStream<MotionGridUnit>)input, FileExtension);
    var outputInfo = await muxer.InitAsync(ct);
    return new MotionGridMuxStream(muxer, outputInfo);
  }

  public OneOf<ISegmentReader, Error> CreateReader(Stream input) =>
    new MotionGridSegmentReader(input);
}
