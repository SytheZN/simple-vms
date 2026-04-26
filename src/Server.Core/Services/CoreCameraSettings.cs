using Server.Plugins;
using Shared.Models;

namespace Server.Core.Services;

public sealed class CoreCameraSettings : IPluginCameraSettings
{
  public const string PluginId = "core";

  private readonly IPluginHost _plugins;

  public CoreCameraSettings(IPluginHost plugins)
  {
    _plugins = plugins;
  }

  public IReadOnlyList<SettingGroup> GetSchema(Guid cameraId) =>
  [
    new SettingGroup
    {
      Key = "recording",
      Order = 0,
      Label = "Recording",
      Fields =
      [
        new SettingField
        {
          Key = "segmentDuration",
          Order = 0,
          Label = "Segment Duration (seconds)",
          Type = "number",
          Description = "How long each recorded segment is. Leave blank to inherit the server default.",
          Required = false
        }
      ]
    },
    new SettingGroup
    {
      Key = "retention",
      Order = 1,
      Label = "Retention",
      Fields =
      [
        new SettingField
        {
          Key = "retentionMode",
          Order = 0,
          Label = "Retention Mode",
          Type = "select",
          Description = "How long recordings are kept on disk.",
          DefaultValue = "default",
          Required = true,
          Options =
          [
            new SettingFieldOption { Value = "default", Label = "Inherit from Server" },
            new SettingFieldOption { Value = "days", Label = "Days" },
            new SettingFieldOption { Value = "bytes", Label = "Bytes" },
            new SettingFieldOption { Value = "percent", Label = "Percent" }
          ]
        },
        new SettingField
        {
          Key = "retentionValue",
          Order = 1,
          Label = "Retention Value",
          Type = "number",
          Description = "Quantity for the selected Mode. Leave blank to inherit.",
          Required = false
        }
      ]
    }
  ];

  public IReadOnlyDictionary<string, string> GetValues(Guid cameraId)
  {
    var camera = _plugins.DataProvider.Cameras.GetByIdAsync(cameraId).GetAwaiter().GetResult();
    if (camera.IsT1)
      return new Dictionary<string, string>();

    var c = camera.AsT0;
    return new Dictionary<string, string>
    {
      ["segmentDuration"] = c.SegmentDuration?.ToString() ?? "",
      ["retentionMode"] = c.RetentionMode.ToString().ToLowerInvariant(),
      ["retentionValue"] = c.RetentionValue == 0 ? "" : c.RetentionValue.ToString()
    };
  }

  public OneOf<Success, Error> ValidateValue(Guid cameraId, string key, string value)
  {
    switch (key)
    {
      case "segmentDuration":
        if (!string.IsNullOrEmpty(value) && !int.TryParse(value, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0040),
            "segmentDuration must be an integer");
        break;
      case "retentionMode":
        if (!Enum.TryParse<RetentionMode>(value, ignoreCase: true, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0041),
            "retentionMode must be one of default, days, bytes, percent");
        break;
      case "retentionValue":
        if (!string.IsNullOrEmpty(value) && !long.TryParse(value, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0042),
            "retentionValue must be a number");
        break;
    }
    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(Guid cameraId, IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validation = ValidateValue(cameraId, key, value);
      if (validation.IsT1) return validation;
    }

    var cameraResult = _plugins.DataProvider.Cameras.GetByIdAsync(cameraId).GetAwaiter().GetResult();
    if (cameraResult.IsT1) return cameraResult.AsT1;
    var camera = cameraResult.AsT0;

    if (values.TryGetValue("segmentDuration", out var sd))
      camera.SegmentDuration = string.IsNullOrEmpty(sd) ? null : int.Parse(sd);
    if (values.TryGetValue("retentionMode", out var rm))
      camera.RetentionMode = Enum.Parse<RetentionMode>(rm, ignoreCase: true);
    if (values.TryGetValue("retentionValue", out var rv))
      camera.RetentionValue = string.IsNullOrEmpty(rv) ? 0 : long.Parse(rv);

    var upsert = _plugins.DataProvider.Cameras.UpdateAsync(camera).GetAwaiter().GetResult();
    return upsert.IsT1 ? upsert.AsT1 : new Success();
  }

  public Task<OneOf<Success, Error>> OnRemovedAsync(Guid cameraId, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
