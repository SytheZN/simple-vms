namespace Shared.Models;

public interface IPlugin
{
  PluginMetadata Metadata { get; }
  OneOf<Success, Error> Initialize(PluginContext context);
  Task<OneOf<Success, Error>> StartAsync(CancellationToken ct);
  Task<OneOf<Success, Error>> StopAsync(CancellationToken ct);
}

public sealed class PluginMetadata
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public required string Version { get; init; }
  public string? Description { get; init; }
}

public sealed class PluginContext
{
  public required IConfig Config { get; init; }
  public required IServerEnvironment Environment { get; init; }
  public IEventBus? EventBus { get; init; }
  public IDataStore? DataStore { get; init; }
  public ICameraRegistry? CameraRegistry { get; init; }
  public IStreamTap? StreamTap { get; init; }
  public IRecordingAccess? RecordingAccess { get; init; }
}
