namespace Shared.Models;

public interface IConfig
{
  T Get<T>(string key, T defaultValue);
  void Set<T>(string key, T value);
}
