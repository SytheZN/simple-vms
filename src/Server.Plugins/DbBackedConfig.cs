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

  public string Get(string key, string defaultValue)
  {
    var result = _repo.GetAsync(_pluginId, key).GetAwaiter().GetResult();
    return result.Match(
      value => value ?? defaultValue,
      _ => defaultValue);
  }

  public void Set(string key, string value)
  {
    _repo.SetAsync(_pluginId, key, value).GetAwaiter().GetResult();
  }
}
