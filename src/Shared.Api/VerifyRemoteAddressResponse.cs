namespace Shared.Api;

public sealed class VerifyRemoteAddressResponse
{
  public required string PublicIp { get; init; }
  public string[]? ResolvedIps { get; init; }
}
