using Shared.Models;

namespace Storage.Filesystem;

public sealed partial class FilesystemPlugin : IPluginSettings
{
  public IReadOnlyList<SettingGroup> GetSchema() =>
  [
    new SettingGroup
    {
      Key = "storage",
      Order = 0,
      Label = "Storage",
      Fields =
      [
        new SettingField
        {
          Key = "path",
          Order = 0,
          Label = "Recordings Path",
          Type = "path",
          Description = "Directory where recording segments are stored",
          DefaultValue = Path.Combine(_environment.DataPath, "recordings"),
          Required = true
        }
      ]
    }
  ];

  public IReadOnlyDictionary<string, object> GetValues() =>
    new Dictionary<string, object>
    {
      ["path"] = _config.Get("path", Path.Combine(_environment.DataPath, "recordings"))
    };

  public OneOf<Success, Error> ValidateValue(string key, object value)
  {
    if (key != "path")
      return new Success();

    var str = value?.ToString();
    if (string.IsNullOrWhiteSpace(str))
      return new Error(Result.BadRequest,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0010),
        "Recordings path is required");

    if (str.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
      return new Error(Result.BadRequest,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0011),
        "Path contains invalid characters");

    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, object> values)
  {
    foreach (var (key, value) in values)
    {
      var validation = ValidateValue(key, value);
      if (validation.IsT1) return validation;
    }

    if (values.TryGetValue("path", out var path))
      _config.Set("path", path.ToString()!);

    return new Success();
  }
}
