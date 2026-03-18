using Shared.Models;

namespace Server.Plugins;

public sealed class InMemoryConfig : IConfig
{
  private readonly Dictionary<string, object> _values = [];

  public T Get<T>(string key, T defaultValue) =>
    _values.TryGetValue(key, out var raw) && raw is T typed ? typed : defaultValue;

  public void Set<T>(string key, T value) =>
    _values[key] = value!;
}
