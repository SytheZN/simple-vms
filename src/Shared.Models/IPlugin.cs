using Microsoft.Extensions.DependencyInjection;

namespace Shared.Models;

public interface IPlugin
{
  PluginMetadata Metadata { get; }
  void ConfigureServices(IServiceCollection services);
  Task StartAsync(CancellationToken ct);
  Task StopAsync(CancellationToken ct);
}

public sealed class PluginMetadata
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public required string Version { get; init; }
  public string? Description { get; init; }
}
