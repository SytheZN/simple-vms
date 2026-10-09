namespace Shared.Models;

public interface IPluginSettings
{
  IReadOnlyList<SettingGroup> GetSchema();
  IReadOnlyDictionary<string, string> GetValues();
  OneOf<Success, Error> ValidateValue(string key, string value);
  OneOf<Success, Error> ValidateGroup(string groupId, IReadOnlyDictionary<string, string> values) =>
    new Success();
  OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, string> values);
}
