using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Analyzer.MotionGridH26x;

public sealed partial class MotionGridH26xPlugin : IDataStreamAnalyzer
{
  public string AnalyzerId => AnalyzerIdValue;
  public IReadOnlyList<string> SupportedCodecs { get; } = ["h264", "h265"];

  public IReadOnlyList<DerivedStreamSpec> GetDerivedStreams(Guid cameraId)
  {
    var camera = LoadCamera(cameraId);
    if (camera == null) return [];

    var specs = new List<DerivedStreamSpec>();
    foreach (var stream in SupportedStreams(camera))
    {
      if (!StreamEnabled(stream.Id)) continue;
      specs.Add(new DerivedStreamSpec
      {
        ParentProfile = stream.Profile,
        Profile = $"{stream.Profile}-motion-grid",
        Kind = StreamKind.Metadata,
        FormatId = "motion-grid",
        Codec = "mgrd"
      });
    }
    return specs;
  }

  private CameraInfo? LoadCamera(Guid cameraId)
  {
    var result = _cameraRegistry.GetCameraAsync(cameraId, CancellationToken.None)
      .GetAwaiter().GetResult();
    return result.Match<CameraInfo?>(
      camera => camera,
      error =>
      {
        if (error.Result != Result.NotFound)
          _logger.LogWarning("Failed to load camera {CameraId} ({Tag}): {Message}",
            cameraId, error.Tag, error.Message);
        return null;
      });
  }

  private bool IsSupported(StreamProfile stream) =>
    stream.IsRootStream &&
    stream.Codec != null &&
    SupportedCodecs.Contains(stream.Codec, StringComparer.OrdinalIgnoreCase);

  internal IReadOnlyList<StreamProfile> SupportedStreams(CameraInfo camera) =>
    camera.Streams.Where(IsSupported).ToList();
}
