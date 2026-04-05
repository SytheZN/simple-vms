namespace Shared.Models;

public interface IPluginSettings
{
  IReadOnlyList<SettingGroup> GetSchema();
  IReadOnlyDictionary<string, string> GetValues();
  OneOf<Success, Error> ValidateValue(string key, string value);
  OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, string> values);
}

public record SettingGroup
{
  public required string Key { get; init; }
  public required int Order { get; init; }
  public required string Label { get; init; }
  public string? Description { get; init; }
  public required IReadOnlyList<SettingField> Fields { get; init; }
}

public record SettingField
{
  public required string Key { get; init; }
  public required int Order { get; init; }
  public required string Label { get; init; }
  public required string Type { get; init; }
  public string? Description { get; init; }
  public string? DefaultValue { get; init; }
  public bool Required { get; init; }
}
