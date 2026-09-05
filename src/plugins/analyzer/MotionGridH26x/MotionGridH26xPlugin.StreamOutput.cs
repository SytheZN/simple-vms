using Shared.Models;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x;

public sealed partial class MotionGridH26xPlugin : IDataStreamAnalyzerStreamOutput
{
  public async Task<OneOf<IDataStream, Error>> StartStreamAsync(
    Guid cameraId, string parentProfile, CancellationToken ct)
  {
    var key = (cameraId, parentProfile);
    if (_workers.TryRemove(key, out var stale))
      await stale.DisposeAsync();

    var tap = await _streamTap.TapAsync(cameraId, parentProfile, ct);
    if (tap.IsT1) return tap.AsT1;

    var cached = FilterSettingsFor(cameraId);
    var initial = new ProcessorSettings(
      cached.Algorithm, cached.WindowFrames, cached.Deblock, cached.Despeckle);
    var processor = new MotionGridProcessor(initial, () =>
    {
      if (!cached.Dirty) return null;
      cached.Dirty = false;
      return new ProcessorSettings(
        cached.Algorithm, cached.WindowFrames, cached.Deblock, cached.Despeckle);
    }, _logger);
    switch (tap.AsT0)
    {
      case IDataStream<H264NalUnit> h264:
      {
        var worker = new MotionGridH264Worker(cameraId, parentProfile, h264, processor, _logger);
        _workers[key] = worker;
        return OneOf<IDataStream, Error>.FromT0(worker);
      }
      case IDataStream<H265NalUnit> h265:
      {
        var worker = new MotionGridH265Worker(cameraId, parentProfile, h265, processor, _logger);
        _workers[key] = worker;
        return OneOf<IDataStream, Error>.FromT0(worker);
      }
      default:
        return Error.Create(ModuleIds.PluginManagement, 0x0061, Result.Unavailable,
          $"Parent stream {cameraId}/{parentProfile} is not H.264 or H.265");
    }
  }

  public bool NeedsRebuild(Guid cameraId, string parentProfile) => false;
}
