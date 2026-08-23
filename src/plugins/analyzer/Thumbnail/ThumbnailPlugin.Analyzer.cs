using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Analyzer.Thumbnail;

public sealed partial class ThumbnailPlugin : IDataStreamAnalyzer
{
  private const string SourceProfileKey = "sourceProfile";

  /// <summary>
  /// Clients locate the gallery preview by this suffix on a metadata stream, so it is part of the
  /// contract with them rather than a naming convenience.
  /// </summary>
  public const string ProfileSuffix = "-thumbnail";

  public string AnalyzerId => AnalyzerIdValue;
  public IReadOnlyList<string> SupportedCodecs { get; } = ["h264", "h265"];

  public IReadOnlyList<DerivedStreamSpec> GetDerivedStreams(Guid cameraId)
  {
    var camera = LoadCamera(cameraId);
    if (camera == null) return [];

    var source = ResolveSource(camera);
    if (source == null) return [];

    return
    [
      new DerivedStreamSpec
      {
        ParentProfile = source.Profile,
        Profile = source.Profile + ProfileSuffix,
        Kind = StreamKind.Metadata,
        FormatId = "mjpeg",
        Codec = "mjpg",
        Recordable = false
      }
    ];
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

  /// <summary>
  /// A stored profile naming a stream the camera no longer exposes is treated as unset, so a
  /// refresh that drops and later restores a profile honours the original choice again.
  /// </summary>
  internal StreamProfile? ResolveSource(CameraInfo camera)
  {
    var supported = SupportedStreams(camera);
    if (supported.Count == 0) return null;

    var stored = _config.Get(SourceKey(camera.Id), "");
    return supported.FirstOrDefault(s => s.Profile == stored)
      ?? supported.OrderBy(PixelCount).First();
  }

  internal static string SourceKey(Guid cameraId) => $"camera/{cameraId}/{SourceProfileKey}";

  private static long PixelCount(StreamProfile stream)
  {
    var resolution = stream.Resolution;
    if (resolution == null) return long.MaxValue;

    var separator = resolution.IndexOf('x', StringComparison.OrdinalIgnoreCase);
    if (separator <= 0) return long.MaxValue;

    return long.TryParse(resolution[..separator], out var width)
      && long.TryParse(resolution[(separator + 1)..], out var height)
        ? width * height
        : long.MaxValue;
  }
}
