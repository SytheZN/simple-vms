using System.Text.Json;
using Shared.Models;

namespace Server.Plugins;

public sealed class DbBackedConfig : IConfig
{
  private readonly IConfigRepository _repo;
  private readonly string _pluginId;

  public DbBackedConfig(IConfigRepository repo, string pluginId)
  {
    _repo = repo;
    _pluginId = pluginId;
  }

  public T Get<T>(string key, T defaultValue)
  {
    var result = _repo.GetAsync(_pluginId, key).GetAwaiter().GetResult();
    return result.Match(
      value => value != null ? JsonSerializer.Deserialize<T>(value) ?? defaultValue : defaultValue,
      _ => defaultValue);
  }

  public void Set<T>(string key, T value)
  {
    var json = JsonSerializer.Serialize(value);
    _repo.SetAsync(_pluginId, key, json).GetAwaiter().GetResult();
  }
}
