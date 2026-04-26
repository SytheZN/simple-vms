namespace Shared.Models;

public interface IPluginStreamSettings
{
  IReadOnlyList<SettingGroup> GetSchema(Guid streamId);
  IReadOnlyDictionary<string, string> GetValues(Guid streamId);
  OneOf<Success, Error> ValidateValue(Guid streamId, string key, string value);
  OneOf<Success, Error> ApplyValues(Guid streamId, IReadOnlyDictionary<string, string> values);
  Task<OneOf<Success, Error>> OnRemovedAsync(Guid streamId, CancellationToken ct);
}
