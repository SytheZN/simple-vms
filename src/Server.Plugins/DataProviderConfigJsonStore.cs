using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Plugins;

public sealed class DataProviderConfigJsonStore
{
  private readonly string _path;
  private DataProviderConfigData _data;

  public string? ActiveProvider => _data.Active;
  public bool Exists => File.Exists(_path);

  public DataProviderConfigJsonStore(string dataPath)
  {
    _path = Path.Combine(dataPath, "dataProviderConfig.json");
    _data = Load();
  }

  public IReadOnlyDictionary<string, string> GetProviderSettings(string assemblyName)
  {
    if (_data.Providers.TryGetValue(assemblyName, out var settings))
      return settings;
    return new Dictionary<string, string>();
  }

  public void SetProviderSettings(string assemblyName, Dictionary<string, string> settings)
  {
    _data.Providers[assemblyName] = settings;
    Save();
  }

  public void SetActive(string assemblyName)
  {
    _data.Active = assemblyName;
    Save();
  }

  private DataProviderConfigData Load()
  {
    if (!File.Exists(_path))
      return new DataProviderConfigData();

    var json = File.ReadAllText(_path);
    return JsonSerializer.Deserialize(json, DataProviderConfigJsonContext.Default.DataProviderConfigData)
      ?? new DataProviderConfigData();
  }

  private void Save()
  {
    var dir = Path.GetDirectoryName(_path);
    if (dir != null)
      Directory.CreateDirectory(dir);

    File.WriteAllText(_path, JsonSerializer.Serialize(_data, DataProviderConfigJsonContext.Default.DataProviderConfigData));
  }
}

internal sealed class DataProviderConfigData
{
  public string? Active { get; set; }
  public Dictionary<string, Dictionary<string, string>> Providers { get; set; } = [];
}

[JsonSourceGenerationOptions(
  PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
  WriteIndented = true,
  DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DataProviderConfigData))]
internal partial class DataProviderConfigJsonContext : JsonSerializerContext;
