using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;
using Shared.Api;
using Shared.Models.Events;

namespace Server.Core.Services;

public sealed class CameraConfigService
{
  private readonly IPluginHost _plugins;
  private readonly IEventBus _eventBus;
  private readonly ILogger<CameraConfigService> _logger;
  private readonly CoreCameraSettings _coreCamera;
  private readonly CoreStreamSettings _coreStream;

  public CameraConfigService(IPluginHost plugins, IEventBus eventBus, ILogger<CameraConfigService> logger)
  {
    _plugins = plugins;
    _eventBus = eventBus;
    _logger = logger;
    _coreCamera = new CoreCameraSettings(plugins);
    _coreStream = new CoreStreamSettings(plugins);
  }

  public async Task<OneOf<CameraConfigSchemaResponse, Error>> GetSchemaAsync(Guid cameraId, CancellationToken ct)
  {
    var streamsResult = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return streamsResult.AsT1;

    var camera = new Dictionary<string, IReadOnlyList<SettingGroup>>();
    foreach (var (id, settings) in CameraSettingsSources())
    {
      var groups = settings.GetSchema(cameraId);
      if (groups.Count == 0) continue;
      camera[id] = groups;
    }

    var streams = new Dictionary<string, Dictionary<string, IReadOnlyList<SettingGroup>>>();
    foreach (var stream in streamsResult.AsT0.Where(s => s.DeletedAt == null))
    {
      var perProfile = new Dictionary<string, IReadOnlyList<SettingGroup>>();
      foreach (var (id, settings) in StreamSettingsSources())
      {
        var groups = settings.GetSchema(stream.Id);
        if (groups.Count == 0) continue;
        perProfile[id] = groups;
      }
      if (perProfile.Count > 0)
        streams[stream.Profile] = perProfile;
    }

    return new CameraConfigSchemaResponse { Camera = camera, Streams = streams };
  }

  public async Task<OneOf<CameraConfigValues, Error>> GetValuesAsync(Guid cameraId, CancellationToken ct)
  {
    var streamsResult = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return streamsResult.AsT1;

    var camera = new Dictionary<string, Dictionary<string, string>>();
    foreach (var (id, settings) in CameraSettingsSources())
    {
      if (settings.GetSchema(cameraId).Count == 0) continue;
      camera[id] = new Dictionary<string, string>(settings.GetValues(cameraId));
    }

    var streams = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
    foreach (var stream in streamsResult.AsT0.Where(s => s.DeletedAt == null))
    {
      var perProfile = new Dictionary<string, Dictionary<string, string>>();
      foreach (var (id, settings) in StreamSettingsSources())
      {
        if (settings.GetSchema(stream.Id).Count == 0) continue;
        perProfile[id] = new Dictionary<string, string>(settings.GetValues(stream.Id));
      }
      if (perProfile.Count > 0)
        streams[stream.Profile] = perProfile;
    }

    return new CameraConfigValues { Camera = camera, Streams = streams };
  }

  public async Task<OneOf<Success, Error>> ApplyAsync(Guid cameraId, CameraConfigValues body, CancellationToken ct)
  {
    var pendingCamera = new List<(string PluginId, IPluginCameraSettings Settings, IReadOnlyDictionary<string, string> Values)>();
    var pendingStream = new List<(string PluginId, IPluginStreamSettings Settings, Guid StreamId, IReadOnlyDictionary<string, string> Values)>();

    foreach (var (pluginId, values) in body.Camera ?? [])
    {
      var settings = ResolveCameraSettings(pluginId);
      if (settings == null)
        return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0030),
          $"Unknown plugin '{pluginId}' in camera section");
      pendingCamera.Add((pluginId, settings, values));
    }

    if (body.Streams?.Count > 0)
    {
      var streamsResult = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
      if (streamsResult.IsT1) return streamsResult.AsT1;
      var byProfile = streamsResult.AsT0
        .Where(s => s.DeletedAt == null)
        .ToDictionary(s => s.Profile);

      foreach (var (profile, perProfile) in body.Streams)
      {
        if (!byProfile.TryGetValue(profile, out var stream))
          return new Error(Result.NotFound, new DebugTag(ModuleIds.CameraManagement, 0x0031),
            $"Profile '{profile}' not found on camera {cameraId}");

        foreach (var (pluginId, values) in perProfile ?? [])
        {
          var settings = ResolveStreamSettings(pluginId);
          if (settings == null)
            return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0032),
              $"Unknown plugin '{pluginId}' in stream section");
          pendingStream.Add((pluginId, settings, stream.Id, values));
        }
      }
    }

    foreach (var (_, settings, values) in pendingCamera)
      foreach (var (key, value) in values)
      {
        var v = settings.ValidateValue(cameraId, key, value);
        if (v.IsT1) return v.AsT1;
      }

    foreach (var (_, settings, streamId, values) in pendingStream)
      foreach (var (key, value) in values)
      {
        var v = settings.ValidateValue(streamId, key, value);
        if (v.IsT1) return v.AsT1;
      }

    var diff = new Dictionary<string, DiffChange>();

    foreach (var (pluginId, settings, values) in pendingCamera)
    {
      var old = settings.GetValues(cameraId);
      foreach (var (key, newValue) in values)
      {
        var oldValue = old.TryGetValue(key, out var v) ? v : null;
        if (oldValue == newValue) continue;
        diff[ConfigDiff.CameraPlugin(cameraId, pluginId, key)] = new DiffChange
        {
          Type = DiffChangeType.Update,
          OldValue = oldValue,
          NewValue = newValue
        };
      }
      var apply = settings.ApplyValues(cameraId, values);
      if (apply.IsT1) return apply.AsT1;
    }

    foreach (var (pluginId, settings, streamId, values) in pendingStream)
    {
      var old = settings.GetValues(streamId);
      foreach (var (key, newValue) in values)
      {
        var oldValue = old.TryGetValue(key, out var v) ? v : null;
        if (oldValue == newValue) continue;
        var path = pluginId == CoreStreamSettings.PluginId
          ? ConfigDiff.StreamField(cameraId, streamId, key)
          : ConfigDiff.StreamPlugin(cameraId, streamId, pluginId, key);
        diff[path] = new DiffChange
        {
          Type = DiffChangeType.Update,
          OldValue = oldValue,
          NewValue = newValue
        };
      }
      var apply = settings.ApplyValues(streamId, values);
      if (apply.IsT1) return apply.AsT1;
    }

    await CameraService.SyncDerivedStreamsAsync(_plugins, cameraId, diff, _logger, ct);

    if (diff.Count > 0)
    {
      await _eventBus.PublishAsync(new CameraConfigChanged
      {
        CameraId = cameraId,
        Diff = diff,
        Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
      }, ct);
    }

    return new Success();
  }

  private IEnumerable<(string Id, IPluginCameraSettings Settings)> CameraSettingsSources()
  {
    yield return (CoreCameraSettings.PluginId, _coreCamera);
    foreach (var entry in _plugins.Plugins)
      if (entry.Plugin is IPluginCameraSettings s)
        yield return (entry.Metadata.Id, s);
  }

  private IEnumerable<(string Id, IPluginStreamSettings Settings)> StreamSettingsSources()
  {
    yield return (CoreStreamSettings.PluginId, _coreStream);
    foreach (var entry in _plugins.Plugins)
      if (entry.Plugin is IPluginStreamSettings s)
        yield return (entry.Metadata.Id, s);
  }

  private IPluginCameraSettings? ResolveCameraSettings(string pluginId) =>
    CameraSettingsSources().FirstOrDefault(p => p.Id == pluginId).Settings;

  private IPluginStreamSettings? ResolveStreamSettings(string pluginId) =>
    StreamSettingsSources().FirstOrDefault(p => p.Id == pluginId).Settings;
}
