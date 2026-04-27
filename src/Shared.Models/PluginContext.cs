namespace Shared.Models;

public sealed class PluginContext
{
  public required IConfig Config { get; init; }
  public required IServerEnvironment Environment { get; init; }
  public required IPluginLoggerFactory LoggerFactory { get; init; }
  public IEventBus? EventBus { get; init; }
  public IDataStore? DataStore { get; init; }
  public ICameraRegistry? CameraRegistry { get; init; }
  public IStreamTap? StreamTap { get; init; }
  public IRecordingAccess? RecordingAccess { get; init; }
}
