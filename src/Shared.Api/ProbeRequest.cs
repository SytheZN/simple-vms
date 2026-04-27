namespace Shared.Api;

public sealed class ProbeRequest
{
  public required string Address { get; init; }
  public string? ProviderId { get; init; }
  public CredentialsDto? Credentials { get; init; }
}
