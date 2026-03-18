using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Plugins;

public sealed class DataProviderConfigJsonStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  private readonly string _path;
  private DataProviderConfigData _data;

  public string? ActiveProvider => _data.Active;
  public bool Exists => File.Exists(_path);

  public DataProviderConfigJsonStore(string dataPath)
  {
    _path = Path.Combine(dataPath, "dataProviderConfig.json");
    _data = Load();
  }

  public IReadOnlyDictionary<string, object> GetProviderSettings(string assemblyName)
  {
    if (_data.Providers.TryGetValue(assemblyName, out var settings))
      return settings;
    return new Dictionary<string, object>();
  }

  public void SetProviderSettings(string assemblyName, Dictionary<string, object> settings)
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
    return JsonSerializer.Deserialize<DataProviderConfigData>(json, JsonOptions)
      ?? new DataProviderConfigData();
  }

  private void Save()
  {
    var dir = Path.GetDirectoryName(_path);
    if (dir != null)
      Directory.CreateDirectory(dir);

    File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOptions));
  }
}

internal sealed class DataProviderConfigData
{
  public string? Active { get; set; }
  public Dictionary<string, Dictionary<string, object>> Providers { get; set; } = [];
}
