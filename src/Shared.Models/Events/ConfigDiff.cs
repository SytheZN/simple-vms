namespace Shared.Models.Events;

public static class ConfigDiff
{
  public const string FieldName = "name";
  public const string FieldAddress = "address";
  public const string FieldProviderId = "providerId";
  public const string FieldCredentials = "credentials";
  public const string FieldRtspPortOverride = "rtspPortOverride";
  public const string FieldCodec = "codec";
  public const string FieldResolution = "resolution";
  public const string FieldFps = "fps";
  public const string FieldBitrate = "bitrate";
  public const string FieldUri = "uri";
  public const string FieldRecordingEnabled = "recordingEnabled";
  public const string FieldRetentionMode = "retentionMode";
  public const string FieldRetentionValue = "retentionValue";
  public const string FieldSegmentDuration = "segmentDuration";

  public static string CameraField(Guid cameraId, string field) =>
    $"cameras[{cameraId}].{field}";

  public static string Stream(Guid cameraId, Guid streamId) =>
    $"cameras[{cameraId}].streams[{streamId}]";

  public static string StreamField(Guid cameraId, Guid streamId, string field) =>
    $"cameras[{cameraId}].streams[{streamId}].{field}";

  public static string CameraPlugin(Guid cameraId, string pluginId, string key) =>
    $"cameras[{cameraId}].plugins[{pluginId}].{key}";

  public static string StreamPlugin(Guid cameraId, Guid streamId, string pluginId, string key) =>
    $"cameras[{cameraId}].streams[{streamId}].plugins[{pluginId}].{key}";

  public static bool AffectsPipelines(this IReadOnlyDictionary<string, DiffChange> diff)
  {
    foreach (var (path, change) in diff)
    {
      if (change.Type is DiffChangeType.Add or DiffChangeType.Remove && IsStreamEntity(path))
        return true;
      if (TouchesPluginConfig(path))
        return true;
      if (EndsWithField(path, FieldCodec)
        || EndsWithField(path, FieldResolution)
        || EndsWithField(path, FieldFps)
        || EndsWithField(path, FieldBitrate)
        || EndsWithField(path, FieldUri))
        return true;
    }
    return false;
  }

  public static bool AffectsRecording(this IReadOnlyDictionary<string, DiffChange> diff)
  {
    foreach (var (path, change) in diff)
    {
      if (change.Type is DiffChangeType.Add or DiffChangeType.Remove && IsStreamEntity(path))
        return true;
      if (TouchesPluginConfig(path))
        return true;
      if (EndsWithField(path, FieldRecordingEnabled)
        || EndsWithField(path, FieldCodec)
        || EndsWithField(path, FieldRetentionMode)
        || EndsWithField(path, FieldRetentionValue)
        || EndsWithField(path, FieldSegmentDuration))
        return true;
    }
    return false;
  }

  private static bool EndsWithField(string path, string field) =>
    path.EndsWith("." + field, StringComparison.Ordinal);

  private static bool IsStreamEntity(string path) =>
    path.EndsWith("]", StringComparison.Ordinal) && path.Contains(".streams[");

  private static bool TouchesPluginConfig(string path) =>
    path.Contains(".plugins[", StringComparison.Ordinal);
}
