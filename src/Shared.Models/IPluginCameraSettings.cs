namespace Shared.Models;

public interface IPluginCameraSettings
{
  IReadOnlyList<SettingGroup> GetSchema(Guid cameraId);
  IReadOnlyDictionary<string, string> GetValues(Guid cameraId);
  OneOf<Success, Error> ValidateValue(Guid cameraId, string key, string value);
  OneOf<Success, Error> ApplyValues(Guid cameraId, IReadOnlyDictionary<string, string> values);
  Task<OneOf<Success, Error>> OnRemovedAsync(Guid cameraId, CancellationToken ct);
}
