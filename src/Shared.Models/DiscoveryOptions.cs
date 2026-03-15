namespace Shared.Models;

public sealed class DiscoveryOptions
{
  public string[]? Subnets { get; init; }
  public string? Username { get; init; }
  public string? Password { get; init; }
}
