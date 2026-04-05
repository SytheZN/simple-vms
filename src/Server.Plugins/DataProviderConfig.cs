using Shared.Models;

namespace Server.Plugins;

public sealed class DataProviderConfig : IConfig
{
  private readonly DataProviderConfigJsonStore _jsonStore;
  private readonly string _assemblyName;

  public DataProviderConfig(DataProviderConfigJsonStore jsonStore, string assemblyName)
  {
    _jsonStore = jsonStore;
    _assemblyName = assemblyName;
  }

  public string Get(string key, string defaultValue)
  {
    var settings = _jsonStore.GetProviderSettings(_assemblyName);
    return settings.TryGetValue(key, out var value) ? value : defaultValue;
  }

  public void Set(string key, string value)
  {
    var settings = new Dictionary<string, string>(_jsonStore.GetProviderSettings(_assemblyName));
    settings[key] = value;
    _jsonStore.SetProviderSettings(_assemblyName, settings);
  }
}
