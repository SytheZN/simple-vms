using Shared.Models;

namespace Server.Plugins;

public sealed class InMemoryConfig : IConfig
{
  private readonly Dictionary<string, string> _values = [];

  public string Get(string key, string defaultValue) =>
    _values.TryGetValue(key, out var value) ? value : defaultValue;

  public void Set(string key, string value) =>
    _values[key] = value;
}
