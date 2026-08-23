using Shared.Models;
using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

public sealed partial class ThumbnailPlugin : IDataStreamAnalyzerStreamOutput
{
  public async Task<OneOf<IDataStream, Error>> StartStreamAsync(
    Guid cameraId, string parentProfile, CancellationToken ct)
  {
    var tap = await _streamTap.TapAsync(cameraId, parentProfile, ct);
    if (tap.IsT1) return tap.AsT1;

    var input = tap.AsT0;
    var isH265 = input.FrameType == typeof(H265NalUnit);
    if (!isH265 && input.FrameType != typeof(H264NalUnit))
    {
      (input as IDisposable)?.Dispose();
      return Error.Create(ModuleIds.PluginThumbnail, 0x0004, Result.Unavailable,
        $"Parent stream {cameraId}/{parentProfile} is not H.264 or H.265");
    }

    var worker = new ThumbnailWorker(
      cameraId, parentProfile, input, isH265,
      () => Size, () => Quality, () => IntervalMicros, _logger, _perfLogger);

    _workers[(cameraId, parentProfile)] = worker;
    return OneOf<IDataStream, Error>.FromT0(worker);
  }
}
