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
          GroupId = RetentionPolicyRules.GroupId,
          Options =
          [
            new SettingFieldOption { Value = "default", Label = "Inherit from Server" },
            new SettingFieldOption { Value = "days", Label = "Days" },
            new SettingFieldOption { Value = "bytes", Label = "Size (GB)" },
            new SettingFieldOption { Value = "percent", Label = "Percent" }
          ]
        },
        new SettingField
        {
          Key = "retentionValue",
          Order = 1,
          Label = "Retention Value",
          Type = "number",
          Description = "Quantity for the selected Mode. Required if Mode is specified.",
          Required = false,
          GroupId = RetentionPolicyRules.GroupId
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
      ["retentionValue"] = RetentionPolicyRules.FormatValue(c.RetentionMode, c.RetentionValue)
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
        if (!Enum.TryParseExact<RetentionMode>(value, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0041),
            "retentionMode must be one of default, days, bytes, percent");
        break;
      case "retentionValue":
        if (!string.IsNullOrEmpty(value) && !RetentionPolicyRules.TryParseValue(value, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0042),
            "retentionValue must be a number");
        break;
    }
    return new Success();
  }

  public OneOf<Success, Error> ValidateGroup(
    Guid cameraId, string groupId, IReadOnlyDictionary<string, string> values) =>
    groupId == RetentionPolicyRules.GroupId
      ? RetentionPolicyRules.Validate(values)
      : new Success();

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
    if (values.ContainsKey(RetentionPolicyRules.ModeKey) || values.ContainsKey(RetentionPolicyRules.ValueKey))
    {
      var display = values.GetValueOrDefault(RetentionPolicyRules.ValueKey)
        ?? RetentionPolicyRules.FormatValue(camera.RetentionMode, camera.RetentionValue);
      if (values.TryGetValue(RetentionPolicyRules.ModeKey, out var rm)
          && Enum.TryParseExact<RetentionMode>(rm, out var mode))
        camera.RetentionMode = mode;
      camera.RetentionValue = RetentionPolicyRules.StoredValue(camera.RetentionMode, display);
    }

    var upsert = _plugins.DataProvider.Cameras.UpdateAsync(camera).GetAwaiter().GetResult();
    return upsert.IsT1 ? upsert.AsT1 : new Success();
  }

  public Task<OneOf<Success, Error>> OnRemovedAsync(Guid cameraId, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
