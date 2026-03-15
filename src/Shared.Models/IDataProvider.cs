namespace Shared.Models;

public interface IDataProvider
{
  string ProviderId { get; }
  ICameraRepository Cameras { get; }
  IStreamRepository Streams { get; }
  ISegmentRepository Segments { get; }
  IKeyframeRepository Keyframes { get; }
  IEventRepository Events { get; }
  IClientRepository Clients { get; }
  ISettingsRepository Settings { get; }
  IPluginDataStore GetPluginStore(string pluginId);
  Task MigrateAsync(CancellationToken ct);
}
