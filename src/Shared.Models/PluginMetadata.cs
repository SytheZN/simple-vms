namespace Shared.Models;

public sealed class PluginMetadata
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public required string Version { get; init; }
  public string? Description { get; init; }
}
