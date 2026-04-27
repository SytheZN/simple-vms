namespace Shared.Api;

public sealed class DiscoveryRequest
{
  public string[]? Subnets { get; init; }
  public int[]? Ports { get; init; }
  public CredentialsDto? Credentials { get; init; }
}
