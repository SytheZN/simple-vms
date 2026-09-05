using Shared.Models;

namespace Analyzer.MotionGridH26x;

public sealed partial class MotionGridH26xPlugin : IPluginSettings
{
  private const string GroupKey = "motion-grid";
  private const string GroupLabel = "Motion Grid";

  private const string DetectionAlgorithmKey = "detectionAlgorithm";
  private const string DefaultDetectionAlgorithm = DetectionAlgorithm.Raw;

  private const string DeblockKey = "deblock";
  private const string DefaultDeblock = "false";

  private const string DespeckleKey = "despeckle";
  private const string DefaultDespeckle = "false";

  private const string WindowFramesKey = "windowFrames";
  private const int DefaultWindowFrames = 10;
  private const int MinWindowFrames = 2;
  private const int MaxWindowFrames = 100;

  private static readonly IReadOnlyList<SettingFieldOption> DetectionAlgorithmOptions =
  [
    new SettingFieldOption { Value = DetectionAlgorithm.Raw, Label = "Raw" },
    new SettingFieldOption { Value = DetectionAlgorithm.Gather, Label = "Gather" },
    new SettingFieldOption { Value = DetectionAlgorithm.Phosphor, Label = "Phosphor" }
  ];

  private static readonly IReadOnlyList<SettingFieldOption> CameraDetectionAlgorithmOptions =
  [
    new SettingFieldOption { Value = "", Label = "Inherit plugin default" },
    .. DetectionAlgorithmOptions
  ];

  internal string PluginDetectionAlgorithm =>
    ResolveAlgorithm(_config.Get(DetectionAlgorithmKey, ""), DefaultDetectionAlgorithm);

  internal bool Deblock =>
    _config.Get(DeblockKey, DefaultDeblock) == "true";

  internal bool Despeckle =>
    _config.Get(DespeckleKey, DefaultDespeckle) == "true";

  internal int WindowFrames =>
    int.TryParse(_config.Get(WindowFramesKey, ""), out var parsed)
      ? parsed
      : DefaultWindowFrames;

  public IReadOnlyList<SettingGroup> GetSchema() => [SettingsGroup(cameraLevel: false)];

  public IReadOnlyDictionary<string, string> GetValues() =>
    new Dictionary<string, string>
    {
      [DetectionAlgorithmKey] = PluginDetectionAlgorithm,
      [DeblockKey] = Deblock.ToString().ToLowerInvariant(),
      [DespeckleKey] = Despeckle.ToString().ToLowerInvariant(),
      [WindowFramesKey] = WindowFrames.ToString()
    };

  public OneOf<Success, Error> ValidateValue(string key, string value) =>
    key switch
    {
      DetectionAlgorithmKey => ValidateDetectionAlgorithm(value),
      DeblockKey => ValidateBoolean(DeblockKey, value),
      DespeckleKey => ValidateBoolean(DespeckleKey, value),
      WindowFramesKey => ValidateWindowFrames(value),
      _ => new Success()
    };

  public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validated = ValidateValue(key, value);
      if (validated.IsT1) return validated.AsT1;
    }

    if (values.TryGetValue(DetectionAlgorithmKey, out var algorithm))
      _config.Set(DetectionAlgorithmKey, algorithm);
    if (values.TryGetValue(DeblockKey, out var deblock))
      _config.Set(DeblockKey, deblock);
    if (values.TryGetValue(DespeckleKey, out var despeckle))
      _config.Set(DespeckleKey, despeckle);
    if (values.TryGetValue(WindowFramesKey, out var frames))
      _config.Set(WindowFramesKey, frames);

    RefreshAllCameraFilterSettings();

    return new Success();
  }

  private void RefreshAllCameraFilterSettings()
  {
    foreach (var (cameraId, cached) in _cameraFilterSettings)
    {
      cached.Algorithm = DetectionAlgorithmFor(cameraId);
      cached.Deblock = ResolveBoolean(CameraKey(cameraId, DeblockKey), Deblock);
      cached.Despeckle = ResolveBoolean(CameraKey(cameraId, DespeckleKey), Despeckle);
      cached.WindowFrames = ResolveWindowFrames(CameraKey(cameraId, WindowFramesKey));
      cached.Dirty = true;
    }
  }

  private SettingGroup SettingsGroup(bool cameraLevel) => new()
  {
    Key = GroupKey,
    Order = 0,
    Label = GroupLabel,
    Fields =
    [
      new SettingField
      {
        Key = DetectionAlgorithmKey,
        Order = 0,
        Label = "Detection Algorithm",
        Description = "Algorithm applied to the per-frame motion grid",
        Type = "select",
        DefaultValue = cameraLevel ? "" : DefaultDetectionAlgorithm,
        Required = !cameraLevel,
        Options = cameraLevel ? CameraDetectionAlgorithmOptions : DetectionAlgorithmOptions
      },
      new SettingField
      {
        Key = DeblockKey,
        Order = 1,
        Label = "Deblock",
        Description = "Temporal deblock filter to suppress single-frame noise",
        Type = cameraLevel ? "tristate" : "boolean",
        DefaultValue = cameraLevel ? "" : DefaultDeblock,
        Required = !cameraLevel
      },
      new SettingField
      {
        Key = DespeckleKey,
        Order = 2,
        Label = "Despeckle",
        Description = "Spatial despeckle filter to remove isolated motion pixels",
        Type = cameraLevel ? "tristate" : "boolean",
        DefaultValue = cameraLevel ? "" : DefaultDespeckle,
        Required = !cameraLevel
      },
      new SettingField
      {
        Key = WindowFramesKey,
        Order = 3,
        Label = "Window (Frames)",
        Description =
          $"Number of frames to analyse over ({MinWindowFrames}-{MaxWindowFrames}){(cameraLevel ? ". Leave blank to inherit the plugin default." : "")}",
        Type = "number",
        DefaultValue = cameraLevel ? WindowFrames.ToString() : DefaultWindowFrames.ToString(),
        Required = !cameraLevel
      }
    ]
  };

  private static string ResolveAlgorithm(string value, string fallback) =>
    DetectionAlgorithmOptions.Any(o => o.Value == value) ? value : fallback;

  private static OneOf<Success, Error> ValidateDetectionAlgorithm(string value) =>
    DetectionAlgorithmOptions.Any(o => o.Value == value)
      ? new Success()
      : Error.Create(ModuleIds.PluginManagement, 0x0066, Result.BadRequest,
        $"{DetectionAlgorithmKey} must be one of: " +
        string.Join(", ", DetectionAlgorithmOptions.Select(o => o.Value)));

  private static OneOf<Success, Error> ValidateBoolean(string key, string value) =>
    value is "true" or "false"
      ? new Success()
      : Error.Create(ModuleIds.PluginManagement, 0x0067, Result.BadRequest,
        $"{key} must be 'true' or 'false'");

  private static OneOf<Success, Error> ValidateWindowFrames(string value) =>
    int.TryParse(value, out var parsed)
    && parsed is >= MinWindowFrames and <= MaxWindowFrames
      ? new Success()
      : Error.Create(ModuleIds.PluginManagement, 0x0065, Result.BadRequest,
        $"Value must be an integer between {MinWindowFrames} and {MaxWindowFrames}");
}
