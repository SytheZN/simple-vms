namespace Shared.Models;

public sealed class DiscoveredCamera
{
  public required string Address { get; init; }
  public string? Hostname { get; init; }
  public string? Name { get; init; }
  public string? Manufacturer { get; init; }
  public string? Model { get; init; }
  public required string ProviderId { get; init; }
  public bool AlreadyAdded { get; init; }
}
