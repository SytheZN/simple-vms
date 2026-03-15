namespace Shared.Models.Dto;

public sealed class PluginListItem
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public required string Version { get; init; }
  public required string Status { get; init; }
  public required string[] ExtensionPoints { get; init; }
}
