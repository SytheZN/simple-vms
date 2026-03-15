using Microsoft.Extensions.DependencyInjection;
namespace Shared.Models;

public interface IPlugin
{
  PluginMetadata Metadata { get; }
  OneOf<Success, Error> ConfigureServices(IServiceCollection services);
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
