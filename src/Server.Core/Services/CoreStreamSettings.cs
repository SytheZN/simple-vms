using Server.Plugins;
using Shared.Models;

namespace Server.Core.Services;

public sealed class CoreStreamSettings : IPluginStreamSettings
{
  public const string PluginId = "core";

  private readonly IPluginHost _plugins;

  public CoreStreamSettings(IPluginHost plugins)
  {
    _plugins = plugins;
  }

  public IReadOnlyList<SettingGroup> GetSchema(Guid streamId)
  {
    var stream = _plugins.DataProvider.Streams.GetByIdAsync(streamId).GetAwaiter().GetResult();
    if (stream.IsT0 && stream.AsT0.Kind == StreamKind.Metadata)
      return [];
    return BuildSchema();
  }

  private static IReadOnlyList<SettingGroup> BuildSchema() =>
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
          Key = "recordingEnabled",
          Order = 0,
          Label = "Record",
          Type = "boolean",
          Description = "Save this stream's video to disk for playback.",
          DefaultValue = "false",
          Required = true
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
          Description = "How long this stream's recordings are kept.",
          DefaultValue = "default",
          Required = true,
          Options =
          [
            new SettingFieldOption { Value = "default", Label = "Inherit from Camera" },
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

  public IReadOnlyDictionary<string, string> GetValues(Guid streamId)
  {
    var stream = _plugins.DataProvider.Streams.GetByIdAsync(streamId).GetAwaiter().GetResult();
    if (stream.IsT1)
      return new Dictionary<string, string>();

    var s = stream.AsT0;
    return new Dictionary<string, string>
    {
      ["recordingEnabled"] = s.RecordingEnabled ? "true" : "false",
      ["retentionMode"] = s.RetentionMode.ToString().ToLowerInvariant(),
      ["retentionValue"] = s.RetentionValue == 0 ? "" : s.RetentionValue.ToString()
    };
  }

  public OneOf<Success, Error> ValidateValue(Guid streamId, string key, string value)
  {
    switch (key)
    {
      case "recordingEnabled":
        if (value != "true" && value != "false")
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0050),
            "recordingEnabled must be 'true' or 'false'");
        break;
      case "retentionMode":
        if (!Enum.TryParse<RetentionMode>(value, ignoreCase: true, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0051),
            "retentionMode must be one of default, days, bytes, percent");
        break;
      case "retentionValue":
        if (!string.IsNullOrEmpty(value) && !long.TryParse(value, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0052),
            "retentionValue must be a number");
        break;
    }
    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(Guid streamId, IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validation = ValidateValue(streamId, key, value);
      if (validation.IsT1) return validation;
    }

    var streamResult = _plugins.DataProvider.Streams.GetByIdAsync(streamId).GetAwaiter().GetResult();
    if (streamResult.IsT1) return streamResult.AsT1;
    var stream = streamResult.AsT0;

    if (values.TryGetValue("recordingEnabled", out var re))
      stream.RecordingEnabled = re == "true";
    if (values.TryGetValue("retentionMode", out var rm))
      stream.RetentionMode = Enum.Parse<RetentionMode>(rm, ignoreCase: true);
    if (values.TryGetValue("retentionValue", out var rv))
      stream.RetentionValue = string.IsNullOrEmpty(rv) ? 0 : long.Parse(rv);

    var upsert = _plugins.DataProvider.Streams.UpsertAsync(stream).GetAwaiter().GetResult();
    return upsert.IsT1 ? upsert.AsT1 : new Success();
  }

  public Task<OneOf<Success, Error>> OnRemovedAsync(Guid streamId, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
