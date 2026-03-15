namespace Shared.Models;

public interface IPluginConfig
{
  T Get<T>(string key, T defaultValue);
  IReadOnlyDictionary<string, object> GetAll();
}
