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

  public IReadOnlyDictionary<string, string> GetValues() =>
    new Dictionary<string, string>
    {
      ["directory"] = _config.Get("directory", _environment.DataPath),
      ["filename"] = _config.Get("filename", "server.db")
    };

  public OneOf<Success, Error> ValidateValue(string key, string value)
  {
    switch (key)
    {
      case "directory":
        if (string.IsNullOrWhiteSpace(value))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.PluginSqliteMigration, 0x0010),
            "Directory is required");
        if (!Directory.Exists(value))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.PluginSqliteMigration, 0x0011),
            $"Directory does not exist: {value}");
        break;
      case "filename":
        if (string.IsNullOrWhiteSpace(value))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.PluginSqliteMigration, 0x0012),
            "Filename is required");
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.PluginSqliteMigration, 0x0013),
            "Filename contains invalid characters");
        break;
    }
    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validation = ValidateValue(key, value);
      if (validation.IsT1) return validation;
    }

    if (values.TryGetValue("directory", out var dir))
      _config.Set("directory", dir);
    if (values.TryGetValue("filename", out var file))
      _config.Set("filename", file);

    return new Success();
  }
}
