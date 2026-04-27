using Server.Plugins;
using Shared.Models;
using Shared.Api;

namespace Server.Core.Services;

public sealed class PluginService
{
  private const string ProviderGroupKey = "provider";
  private const string ActiveFieldKey = "active";

  private readonly IPluginHost _host;
  private readonly DataProviderConfigJsonStore _dataProviderConfig;

  public PluginService(IPluginHost host, DataProviderConfigJsonStore dataProviderConfig)
  {
    _host = host;
    _dataProviderConfig = dataProviderConfig;
  }

  public OneOf<IReadOnlyList<PluginDto>, Error> GetAll(string? type = null)
  {
    var plugins = (IEnumerable<PluginEntry>)_host.Plugins;
    if (type != null)
      plugins = plugins.Where(p => p.ExtensionPoints.Contains(type, StringComparer.OrdinalIgnoreCase));
    var items = plugins.Select(ToDto).OrderBy(p => p.Name).ToList();
    return items;
  }

  public OneOf<PluginDto, Error> GetById(string id)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0001),
        $"Plugin '{id}' not found");

    return ToDto(entry);
  }

  public OneOf<IReadOnlyList<SettingGroup>, Error> GetConfigSchema(string id)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0004),
        $"Plugin '{id}' not found");

    var pluginGroups = entry.Plugin is IPluginSettings settings
      ? settings.GetSchema().ToList()
      : [];

    if (!IsDataPlugin(entry))
      return pluginGroups;

    var combined = new List<SettingGroup> { ActiveProviderGroup() };
    combined.AddRange(pluginGroups);
    return combined;
  }

  public OneOf<IReadOnlyDictionary<string, string>, Error> GetConfigValues(string id)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0007),
        $"Plugin '{id}' not found");

    var values = entry.Plugin is IPluginSettings settings
      ? new Dictionary<string, string>(settings.GetValues())
      : [];

    if (IsDataPlugin(entry))
      values[ActiveFieldKey] = (_dataProviderConfig.ActiveProvider == id).ToString().ToLowerInvariant();
    else if (entry.Plugin is not IPluginSettings)
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.PluginManagement, 0x0008),
        $"Plugin '{id}' does not support settings");

    return values;
  }

  public OneOf<Success, Error> ApplyConfigValues(
    string id, IReadOnlyDictionary<string, string> values)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0009),
        $"Plugin '{id}' not found");

    var pluginValues = values;
    string? activeRaw = null;
    if (IsDataPlugin(entry) && values.TryGetValue(ActiveFieldKey, out var av))
    {
      activeRaw = av;
      pluginValues = values.Where(kv => kv.Key != ActiveFieldKey)
        .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    if (entry.Plugin is IPluginSettings settings)
    {
      var apply = settings.ApplyValues(pluginValues);
      if (apply.IsT1) return apply.AsT1;
    }
    else if (pluginValues.Count > 0)
    {
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.PluginManagement, 0x000A),
        $"Plugin '{id}' does not support settings");
    }

    if (activeRaw != null)
    {
      var enable = ParseEnableOnly(activeRaw, _dataProviderConfig.ActiveProvider == id);
      if (enable.IsT1) return enable.AsT1;
      if (enable.AsT0 && _dataProviderConfig.ActiveProvider != id)
        _dataProviderConfig.SetActive(id);
    }

    return new Success();
  }

  public OneOf<Success, Error> ValidateField(string id, string key, string value)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x000B),
        $"Plugin '{id}' not found");

    if (IsDataPlugin(entry) && key == ActiveFieldKey)
    {
      var enable = ParseEnableOnly(value, _dataProviderConfig.ActiveProvider == id);
      return enable.Match<OneOf<Success, Error>>(_ => new Success(), e => e);
    }

    if (entry.Plugin is not IPluginSettings settings)
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.PluginManagement, 0x000C),
        $"Plugin '{id}' does not support settings");

    return settings.ValidateValue(key, value);
  }

  public async Task<OneOf<Success, Error>> UserStartAsync(string id, CancellationToken ct)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0002),
        $"Plugin '{id}' not found");

    if (entry.Plugin is not IUserStartable startable)
      return new Error(
        Result.Unavailable,
        new DebugTag(ModuleIds.PluginManagement, 0x0005),
        $"Plugin '{id}' does not support user-initiated start");

    return await startable.UserStartAsync(ct);
  }

  public async Task<OneOf<Success, Error>> UserStopAsync(string id, CancellationToken ct)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0003),
        $"Plugin '{id}' not found");

    if (entry.Plugin is not IUserStartable startable)
      return new Error(
        Result.Unavailable,
        new DebugTag(ModuleIds.PluginManagement, 0x0006),
        $"Plugin '{id}' does not support user-initiated stop");

    return await startable.UserStopAsync(ct);
  }

  private static bool IsDataPlugin(PluginEntry entry) =>
    entry.ExtensionPoints.Contains("data");

  private static SettingGroup ActiveProviderGroup() => new()
  {
    Key = ProviderGroupKey,
    Order = -1,
    Label = "Provider",
    Fields =
    [
      new SettingField
      {
        Key = ActiveFieldKey,
        Order = 0,
        Label = "Set as active data provider",
        Type = "boolean-enable-only",
        DefaultValue = "false",
        Required = true
      }
    ]
  };

  private static OneOf<bool, Error> ParseEnableOnly(string raw, bool currentlyActive)
  {
    if (raw != "true" && raw != "false")
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.PluginManagement, 0x000D),
        $"'{ActiveFieldKey}' must be 'true' or 'false'");

    var enable = raw == "true";
    if (!enable && currentlyActive)
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.PluginManagement, 0x000E),
        "Cannot deactivate the current data provider; activate a different provider instead");

    return enable;
  }

  private static PluginDto ToDto(PluginEntry entry) =>
    new()
    {
      Id = entry.Metadata.Id,
      Name = entry.Metadata.Name,
      Description = entry.Metadata.Description,
      Version = entry.Metadata.Version,
      Status = entry.State.ToString().ToLowerInvariant(),
      ExtensionPoints = entry.ExtensionPoints,
      UserStartable = entry.Plugin is IUserStartable,
      HasSettings = entry.Plugin is IPluginSettings || IsDataPlugin(entry),
      HasCameraSettings = entry.Plugin is IPluginCameraSettings,
      HasStreamSettings = entry.Plugin is IPluginStreamSettings
    };
}
