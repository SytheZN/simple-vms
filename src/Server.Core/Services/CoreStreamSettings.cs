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
            ["retentionValue"] = s.RetentionValue == 0 ? "" : s.RetentionValue.ToString()
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

    return _plugins.DataProvider.Streams.GetByIdAsync(streamId).GetAwaiter().GetResult().Match<OneOf<Success, Error>>(
      stream =>
      {
        if (values.TryGetValue("recordingEnabled", out var re))
          stream.RecordingEnabled = re == "true";
        if (values.TryGetValue("retentionMode", out var rm))
          stream.RetentionMode = Enum.Parse<RetentionMode>(rm, ignoreCase: true);
        if (values.TryGetValue("retentionValue", out var rv))
          stream.RetentionValue = string.IsNullOrEmpty(rv) ? 0 : long.Parse(rv);

        return _plugins.DataProvider.Streams.UpsertAsync(stream).GetAwaiter().GetResult().Match<OneOf<Success, Error>>(
          _ => new Success(),
          err => err);
      },
      err => err);
  }

  public Task<OneOf<Success, Error>> OnRemovedAsync(Guid streamId, CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
