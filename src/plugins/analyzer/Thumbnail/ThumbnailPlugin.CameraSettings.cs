using Shared.Models;

namespace Analyzer.Thumbnail;

public sealed partial class ThumbnailPlugin : IPluginCameraSettings
{
  public IReadOnlyList<SettingGroup> GetSchema(Guid cameraId)
  {
    var camera = LoadCamera(cameraId);
    if (camera == null) return [];

    var supported = SupportedStreams(camera);
    if (supported.Count == 0) return [];

    return
    [
      new SettingGroup
      {
        Key = "thumbnail",
        Order = 0,
        Label = "Thumbnail",
        Fields =
        [
          new SettingField
          {
            Key = SourceProfileKey,
            Order = 0,
            Label = "Preview source",
            Description = "Stream the gallery preview is decoded from",
            Type = "select",
            DefaultValue = ResolveSource(camera)?.Profile,
            Required = true,
            Options = supported
              .Select(s => new SettingFieldOption
              {
                Value = s.Profile,
                Label = string.IsNullOrEmpty(s.Resolution) ? s.Profile : $"{s.Profile} ({s.Resolution})"
              })
              .ToList()
          }
        ]
      }
    ];
  }

  /// <summary>
  /// Reports the effective source rather than the stored one, so a value naming a stream the
  /// camera no longer exposes does not surface as a selection the schema has no option for.
  /// </summary>
  public IReadOnlyDictionary<string, string> GetValues(Guid cameraId)
  {
    var camera = LoadCamera(cameraId);
    var source = camera == null ? null : ResolveSource(camera);
    return source == null
      ? new Dictionary<string, string>()
      : new Dictionary<string, string> { [SourceProfileKey] = source.Profile };
  }

  public OneOf<Success, Error> ValidateValue(Guid cameraId, string key, string value)
  {
    if (key != SourceProfileKey)
      return new Success();

    var camera = LoadCamera(cameraId);
    if (camera == null)
      return Error.Create(ModuleIds.PluginThumbnail, 0x0001, Result.NotFound,
        $"Camera {cameraId} not found");

    if (!SupportedStreams(camera).Any(s => s.Profile == value))
      return Error.Create(ModuleIds.PluginThumbnail, 0x0002, Result.BadRequest,
        $"'{value}' is not a supported source stream for camera {cameraId}");

    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(Guid cameraId, IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validated = ValidateValue(cameraId, key, value);
      if (validated.IsT1) return validated.AsT1;
    }

    if (values.TryGetValue(SourceProfileKey, out var profile))
      _config.Set(SourceKey(cameraId), profile);

    return new Success();
  }

  public Task<OneOf<Success, Error>> OnRemovedAsync(Guid cameraId, CancellationToken ct)
  {
    _config.Set(SourceKey(cameraId), "");
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }
}
