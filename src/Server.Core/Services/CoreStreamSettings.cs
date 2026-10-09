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
    var result = _plugins.DataProvider.Streams.GetByIdAsync(streamId).GetAwaiter().GetResult();
    return result.Match<IReadOnlyList<SettingGroup>>(
      s => s.Kind == StreamKind.Metadata ? [] : FullSchema,
      _ => FullSchema);
  }

  private static readonly SettingGroup RecordingGroup = new()
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
  };

  private static readonly SettingGroup RetentionGroup = new()
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
        GroupId = RetentionPolicyRules.GroupId,
        Options =
        [
          new SettingFieldOption { Value = "default", Label = "Inherit from Camera" },
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
  };

  private static readonly IReadOnlyList<SettingGroup> FullSchema = [RecordingGroup, RetentionGroup];

  public IReadOnlyDictionary<string, string> GetValues(Guid streamId)
  {
    var result = _plugins.DataProvider.Streams.GetByIdAsync(streamId).GetAwaiter().GetResult();
    return result.Match<IReadOnlyDictionary<string, string>>(
      s => s.Kind == StreamKind.Metadata
        ? new Dictionary<string, string>()
        : new Dictionary<string, string>
          {
            ["recordingEnabled"] = s.RecordingEnabled ? "true" : "false",
            ["retentionMode"] = s.RetentionMode.ToString().ToLowerInvariant(),
            ["retentionValue"] = RetentionPolicyRules.FormatValue(s.RetentionMode, s.RetentionValue)
          },
      _ => new Dictionary<string, string>());
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
        if (!Enum.TryParseExact<RetentionMode>(value, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0051),
            "retentionMode must be one of default, days, bytes, percent");
        break;
      case "retentionValue":
        if (!string.IsNullOrEmpty(value) && !RetentionPolicyRules.TryParseValue(value, out _))
          return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0052),
            "retentionValue must be a number");
        break;
    }
    return new Success();
  }

  public OneOf<Success, Error> ValidateGroup(
    Guid streamId, string groupId, IReadOnlyDictionary<string, string> values) =>
    groupId == RetentionPolicyRules.GroupId
      ? RetentionPolicyRules.Validate(values)
      : new Success();

  public OneOf<Success, Error> ApplyValues(Guid streamId, IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validation = ValidateValue(streamId, key, value);
      if (validation.IsT1) return validation;
    }

    return _plugins.DataProvider.Streams.GetByIdAsync(streamId).GetAwaiter().GetResult().Match<OneOf<Success, Error>>(
      stream =>
      {
        if (values.TryGetValue("recordingEnabled", out var re))
          stream.RecordingEnabled = re == "true";
        if (values.ContainsKey(RetentionPolicyRules.ModeKey) || values.ContainsKey(RetentionPolicyRules.ValueKey))
        {
          var display = values.GetValueOrDefault(RetentionPolicyRules.ValueKey)
            ?? RetentionPolicyRules.FormatValue(stream.RetentionMode, stream.RetentionValue);
          if (values.TryGetValue(RetentionPolicyRules.ModeKey, out var rm)
              && Enum.TryParseExact<RetentionMode>(rm, out var mode))
            stream.RetentionMode = mode;
          stream.RetentionValue = RetentionPolicyRules.StoredValue(stream.RetentionMode, display);
        }

        return _plugins.DataProvider.Streams.UpsertAsync(stream).GetAwaiter().GetResult().Match<OneOf<Success, Error>>(
          _ => new Success(),
          err => err);
      },
      err => err);
  }

  public Task<OneOf<Success, Error>> OnRemovedAsync(Guid streamId, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
