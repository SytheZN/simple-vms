using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class PluginService
{
  private readonly PluginHost _host;
  private readonly IDataProvider _data;

  public PluginService(PluginHost host, IDataProvider data)
  {
    _host = host;
    _data = data;
  }

  public OneOf<IReadOnlyList<PluginListItem>, Error> GetAll()
  {
    var items = _host.Plugins.Select(ToDto).ToList();
    return (OneOf<IReadOnlyList<PluginListItem>, Error>)items;
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

  public async Task<OneOf<Success, Error>> UpdateConfigAsync(
    string id, Dictionary<string, object> config, CancellationToken ct)
  {
    var entry = _host.Plugins.FirstOrDefault(p => p.Metadata.Id == id);
    if (entry == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0004),
        $"Plugin '{id}' not found");

    var store = _data.GetPluginStore(id);
    foreach (var (key, value) in config)
      await store.SetAsync(key, value, ct);

    return new Success();
  }

  public async Task<OneOf<Success, Error>> StartAsync(string id, CancellationToken ct)
  {
    try
    {
      await _host.StartPluginAsync(id, ct);
      return new Success();
    }
    catch (ArgumentException)
    {
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0002),
        $"Plugin '{id}' not found");
    }
  }

  public async Task<OneOf<Success, Error>> StopAsync(string id)
  {
    try
    {
      await _host.StopPluginAsync(id);
      return new Success();
    }
    catch (ArgumentException)
    {
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.PluginManagement, 0x0003),
        $"Plugin '{id}' not found");
    }
  }

  private static PluginListItem ToDto(PluginEntry entry) =>
    new()
    {
      Id = entry.Metadata.Id,
      Name = entry.Metadata.Name,
      Version = entry.Metadata.Version,
      Status = entry.State.ToString().ToLowerInvariant(),
      ExtensionPoints = entry.ExtensionPoints
    };
}
