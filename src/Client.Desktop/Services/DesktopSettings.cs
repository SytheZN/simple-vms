using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Core.Events;

namespace Client.Desktop.Services;

public sealed partial class DesktopSettings
{
  private static readonly string FilePath = Path.Combine(
    GetConfigDir(), "settings.json");

  public List<NotificationRule> NotificationRules { get; set; } = [];
  public bool MinimizeOnClose { get; set; }
  public int GalleryColumns { get; set; } = 3;
  public int LastSuccessfulAddressIndex { get; set; } = -1;
  public bool ReprobeEnabled { get; set; }

  public void Load()
  {
    if (!File.Exists(FilePath)) return;
    var json = File.ReadAllBytes(FilePath);
    ApplyData(JsonSerializer.Deserialize(json, DesktopSettingsJsonContext.Default.DesktopSettingsData));
  }

  public async Task LoadAsync()
  {
    if (!File.Exists(FilePath)) return;
    var json = await File.ReadAllBytesAsync(FilePath);
    ApplyData(JsonSerializer.Deserialize(json, DesktopSettingsJsonContext.Default.DesktopSettingsData));
  }

  private void ApplyData(DesktopSettingsData? data)
  {
    if (data == null) return;
    NotificationRules = data.NotificationRules ?? [];
    MinimizeOnClose = data.MinimizeOnClose;
    GalleryColumns = data.GalleryColumns > 0 ? data.GalleryColumns : 3;
    LastSuccessfulAddressIndex = data.LastSuccessfulAddressIndex;
    ReprobeEnabled = data.ReprobeEnabled;
  }

  public async Task SaveAsync()
  {
    var data = new DesktopSettingsData
    {
      NotificationRules = NotificationRules,
      MinimizeOnClose = MinimizeOnClose,
      GalleryColumns = GalleryColumns,
      LastSuccessfulAddressIndex = LastSuccessfulAddressIndex,
      ReprobeEnabled = ReprobeEnabled
    };
    var json = JsonSerializer.SerializeToUtf8Bytes(data, DesktopSettingsJsonContext.Default.DesktopSettingsData);
    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
    await File.WriteAllBytesAsync(FilePath, json);
  }

  public static string ConfigDir => GetConfigDir();

  private static string GetConfigDir()
  {
    if (OperatingSystem.IsWindows())
      return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleVMS");
    if (OperatingSystem.IsMacOS())
      return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "SimpleVMS");
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "simplevms");
  }

  internal sealed class DesktopSettingsData
  {
    public List<NotificationRule>? NotificationRules { get; set; }
    public bool MinimizeOnClose { get; set; }
    public int GalleryColumns { get; set; }
    public int LastSuccessfulAddressIndex { get; set; }
    public bool ReprobeEnabled { get; set; }
  }

  [JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
  [JsonSerializable(typeof(DesktopSettingsData))]
  internal sealed partial class DesktopSettingsJsonContext : JsonSerializerContext;
}
