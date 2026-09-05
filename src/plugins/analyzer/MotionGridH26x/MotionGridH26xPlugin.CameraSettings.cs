using Shared.Models;

namespace Analyzer.MotionGridH26x;

public sealed partial class MotionGridH26xPlugin : IPluginCameraSettings
{
  internal string DetectionAlgorithmFor(Guid cameraId) =>
    ResolveAlgorithm(_config.Get(CameraKey(cameraId, DetectionAlgorithmKey), ""), PluginDetectionAlgorithm);

  private CameraFilterSettings FilterSettingsFor(Guid cameraId) =>
    _cameraFilterSettings.GetOrAdd(cameraId, id => new CameraFilterSettings
    {
      Algorithm = DetectionAlgorithmFor(id),
      Deblock = ResolveBoolean(CameraKey(id, DeblockKey), Deblock),
      Despeckle = ResolveBoolean(CameraKey(id, DespeckleKey), Despeckle),
      WindowFrames = ResolveWindowFrames(CameraKey(id, WindowFramesKey))
    });

  private bool ResolveBoolean(string key, bool pluginDefault) =>
    _config.Get(key, "") switch
    {
      "true" => true,
      "false" => false,
      _ => pluginDefault
    };

  private int ResolveWindowFrames(string key) =>
    int.TryParse(_config.Get(key, ""), out var parsed) ? parsed : WindowFrames;

  IReadOnlyList<SettingGroup> IPluginCameraSettings.GetSchema(Guid cameraId)
  {
    var camera = LoadCamera(cameraId);
    return camera != null && SupportedStreams(camera).Count > 0
      ? [SettingsGroup(cameraLevel: true)]
      : [];
  }

  IReadOnlyDictionary<string, string> IPluginCameraSettings.GetValues(Guid cameraId) =>
    new Dictionary<string, string>
    {
      [DetectionAlgorithmKey] = _config.Get(CameraKey(cameraId, DetectionAlgorithmKey), ""),
      [DeblockKey] = _config.Get(CameraKey(cameraId, DeblockKey), ""),
      [DespeckleKey] = _config.Get(CameraKey(cameraId, DespeckleKey), ""),
      [WindowFramesKey] = _config.Get(CameraKey(cameraId, WindowFramesKey), "")
    };

  OneOf<Success, Error> IPluginCameraSettings.ValidateValue(Guid cameraId, string key, string value) =>
    ValidateCameraValue(key, value);

  OneOf<Success, Error> IPluginCameraSettings.ApplyValues(
    Guid cameraId, IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validated = ValidateCameraValue(key, value);
      if (validated.IsT1) return validated.AsT1;
    }

    if (values.TryGetValue(DetectionAlgorithmKey, out var algorithm))
      _config.Set(CameraKey(cameraId, DetectionAlgorithmKey), algorithm);
    if (values.TryGetValue(DeblockKey, out var deblock))
      _config.Set(CameraKey(cameraId, DeblockKey), deblock);
    if (values.TryGetValue(DespeckleKey, out var despeckle))
      _config.Set(CameraKey(cameraId, DespeckleKey), despeckle);
    if (values.TryGetValue(WindowFramesKey, out var frames))
      _config.Set(CameraKey(cameraId, WindowFramesKey), frames);

    var cached = FilterSettingsFor(cameraId);
    cached.Algorithm = DetectionAlgorithmFor(cameraId);
    cached.Deblock = ResolveBoolean(CameraKey(cameraId, DeblockKey), Deblock);
    cached.Despeckle = ResolveBoolean(CameraKey(cameraId, DespeckleKey), Despeckle);
    cached.WindowFrames = ResolveWindowFrames(CameraKey(cameraId, WindowFramesKey));
    cached.Dirty = true;

    return new Success();
  }

  Task<OneOf<Success, Error>> IPluginCameraSettings.OnRemovedAsync(Guid cameraId, CancellationToken ct)
  {
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }

  private OneOf<Success, Error> ValidateCameraValue(string key, string value) =>
    key is DetectionAlgorithmKey or DeblockKey or DespeckleKey or WindowFramesKey && string.IsNullOrEmpty(value)
      ? new Success()
      : ValidateValue(key, value);

  private static string CameraKey(Guid cameraId, string key) =>
    $"camera/{cameraId}/{key}";

}
