using Shared.Models;

namespace Data.Sqlite;

public sealed partial class SqliteProvider : IPluginSettings
{
  public IReadOnlyList<SettingGroup> GetSchema() =>
  [
    new SettingGroup
    {
      Key = "database",
      Order = 0,
      Label = "Database",
      Fields =
      [
        new SettingField
        {
          Key = "directory",
          Order = 0,
          Label = "Directory",
          Type = "path",
          Description = "Directory where the database file is stored",
          DefaultValue = _environment.DataPath,
          Required = true
        },
        new SettingField
        {
          Key = "filename",
          Order = 1,
          Label = "Filename",
          Type = "string",
          Description = "Database filename",
          DefaultValue = "server.db",
          Required = true
        }
      ]
    }
  ];

  public IReadOnlyDictionary<string, object> GetValues() =>
    new Dictionary<string, object>
    {
      ["directory"] = _config.Get("directory", _environment.DataPath),
      ["filename"] = _config.Get("filename", "server.db")
    };

  public OneOf<Success, Error> ValidateValue(string key, object value)
  {
    var str = value?.ToString();
    switch (key)
    {
      case "directory":
        if (string.IsNullOrWhiteSpace(str))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.PluginSqliteMigration, 0x0010),
            "Directory is required");
        if (!Directory.Exists(str))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.PluginSqliteMigration, 0x0011),
            $"Directory does not exist: {str}");
        break;
      case "filename":
        if (string.IsNullOrWhiteSpace(str))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.PluginSqliteMigration, 0x0012),
            "Filename is required");
        if (str.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.PluginSqliteMigration, 0x0013),
            "Filename contains invalid characters");
        break;
    }
    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, object> values)
  {
    foreach (var (key, value) in values)
    {
      var validation = ValidateValue(key, value);
      if (validation.IsT1) return validation;
    }

    if (values.TryGetValue("directory", out var dir))
      _config.Set("directory", dir.ToString()!);
    if (values.TryGetValue("filename", out var file))
      _config.Set("filename", file.ToString()!);

    return new Success();
  }
}
