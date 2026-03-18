using System.Text.Json;
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

  public T Get<T>(string key, T defaultValue)
  {
    var settings = _jsonStore.GetProviderSettings(_assemblyName);
    if (!settings.TryGetValue(key, out var raw))
      return defaultValue;

    if (raw is T typed)
      return typed;

    if (raw is JsonElement element)
    {
      var deserialized = element.Deserialize<T>();
      if (deserialized != null)
        return deserialized;
    }

    try
    {
      return (T)Convert.ChangeType(raw, typeof(T));
    }
    catch
    {
      return defaultValue;
    }
  }

  public void Set<T>(string key, T value)
  {
    var settings = new Dictionary<string, object>(_jsonStore.GetProviderSettings(_assemblyName)
      .Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value)));
    settings[key] = value!;
    _jsonStore.SetProviderSettings(_assemblyName, settings);
  }
}
