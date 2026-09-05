using Shared.Models;

namespace Analyzer.Thumbnail;

public sealed partial class ThumbnailPlugin : IPluginSettings
{
  private const string SizeKey = "size";
  private const string QualityKey = "quality";
  private const string IntervalKey = "interval";

  private const int DefaultSize = 240;
  private const int DefaultQuality = 70;
  private const int DefaultInterval = 0;

  private const int MinSize = 16;
  private const int MaxSize = 1920;

  internal int Size => _cachedSize;
  internal int Quality => _cachedQuality;
  internal ulong IntervalMicros => (ulong)_cachedInterval * 1_000_000UL;

  public IReadOnlyList<SettingGroup> GetSchema() =>
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
          Key = SizeKey,
          Order = 0,
          Label = "Bounding size",
          Description = $"Longest edge in pixels ({MinSize}-{MaxSize}); aspect ratio is preserved",
          Type = "number",
          DefaultValue = DefaultSize.ToString(),
          Required = true
        },
        new SettingField
        {
          Key = QualityKey,
          Order = 1,
          Label = "JPEG quality",
          Description = "1-100",
          Type = "number",
          DefaultValue = DefaultQuality.ToString(),
          Required = true
        },
        new SettingField
        {
          Key = IntervalKey,
          Order = 2,
          Label = "Interval",
          Description = "Seconds between previews; 0 uses every keyframe",
          Type = "number",
          DefaultValue = DefaultInterval.ToString(),
          Required = true
        }
      ]
    }
  ];

  public IReadOnlyDictionary<string, string> GetValues() =>
    new Dictionary<string, string>
    {
      [SizeKey] = Size.ToString(),
      [QualityKey] = Quality.ToString(),
      [IntervalKey] = (IntervalMicros / 1_000_000UL).ToString()
    };

  public OneOf<Success, Error> ValidateValue(string key, string value) => key switch
  {
    SizeKey => ValidateRange(key, value, MinSize, MaxSize),
    QualityKey => ValidateRange(key, value, 1, 100),
    IntervalKey => ValidateRange(key, value, 0, int.MaxValue),
    _ => new Success()
  };

  public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validated = ValidateValue(key, value);
      if (validated.IsT1) return validated.AsT1;
    }

    foreach (var (key, value) in values)
      if (key is SizeKey or QualityKey or IntervalKey)
        _config.Set(key, value);

    RefreshCachedSettings();

    return new Success();
  }

  private void RefreshCachedSettings()
  {
    _cachedSize = ReadInt(SizeKey, DefaultSize);
    _cachedQuality = ReadInt(QualityKey, DefaultQuality);
    _cachedInterval = ReadInt(IntervalKey, DefaultInterval);
  }

  private static OneOf<Success, Error> ValidateRange(string key, string value, int min, int max) =>
    int.TryParse(value, out var parsed) && parsed >= min && parsed <= max
      ? new Success()
      : Error.Create(ModuleIds.PluginThumbnail, 0x0003, Result.BadRequest,
        $"{key} must be an integer between {min} and {max}");

  private int ReadInt(string key, int fallback) =>
    int.TryParse(_config.Get(key, ""), out var parsed) ? parsed : fallback;
}
