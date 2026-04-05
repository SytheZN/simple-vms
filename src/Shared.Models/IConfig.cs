namespace Shared.Models;

public interface IConfig
{
  string Get(string key, string defaultValue);
  void Set(string key, string value);
}
