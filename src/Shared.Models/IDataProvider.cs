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
  IConfigRepository Config { get; }
  IDataStore GetDataStore(string pluginId);
}
