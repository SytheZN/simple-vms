using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class PluginService
{
  private readonly IPluginHost _host;

  public PluginService(IPluginHost host)
  {
    _host = host;
  }

  public OneOf<IReadOnlyList<PluginListItem>, Error> GetAll(string? type = null)
  {
    var plugins = (IEnumerable<PluginEntry>)_host.Plugins;
    if (type != null)
      plugins = plugins.Where(p => p.ExtensionPoints.Contains(type, StringComparer.OrdinalIgnoreCase));
    var items = plugins.Select(ToDto).OrderBy(p => p.Name).ToList();
    return items;
  }

  public OneOf<PluginListItem, Error> GetById(string id)
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

    if (entry.Plugin is not IPluginSettings settings)
      return Array.Empty<SettingGroup>();

    return settings.GetSchema().ToList();
  }

  public OneOf<IReadOnlyDictionary<string, object>, Error> GetConfigValues(string id)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0007),
        $"Plugin '{id}' not found");

    if (entry.Plugin is not IPluginSettings settings)
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.PluginManagement, 0x0008),
        $"Plugin '{id}' does not support settings");

    return settings.GetValues().ToDictionary();
  }

  public OneOf<Success, Error> ApplyConfigValues(
    string id, IReadOnlyDictionary<string, object> values)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0009),
        $"Plugin '{id}' not found");

    if (entry.Plugin is not IPluginSettings settings)
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.PluginManagement, 0x000A),
        $"Plugin '{id}' does not support settings");

    return settings.ApplyValues(values);
  }

  public OneOf<Success, Error> ValidateField(string id, string key, object value)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x000B),
        $"Plugin '{id}' not found");

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

  private static PluginListItem ToDto(PluginEntry entry) =>
    new()
    {
      Id = entry.Metadata.Id,
      Name = entry.Metadata.Name,
      Description = entry.Metadata.Description,
      Version = entry.Metadata.Version,
      Status = entry.State.ToString().ToLowerInvariant(),
      ExtensionPoints = entry.ExtensionPoints,
      UserStartable = entry.Plugin is IUserStartable,
      HasSettings = entry.Plugin is IPluginSettings
    };
}
