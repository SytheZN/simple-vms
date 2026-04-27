namespace Shared.Models;

public interface IPlugin
{
  PluginMetadata Metadata { get; }
  OneOf<Success, Error> Initialize(PluginContext context);
  Task<OneOf<Success, Error>> StartAsync(CancellationToken ct);
  Task<OneOf<Success, Error>> StopAsync(CancellationToken ct);
}
