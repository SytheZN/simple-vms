namespace Shared.Models.Dto;

public sealed class DiscoveryRequest
{
  public string[]? Subnets { get; init; }
  public CredentialsDto? Credentials { get; init; }
}

public sealed class DiscoveredCameraDto
{
  public required string Address { get; init; }
  public string? Name { get; init; }
  public string? Manufacturer { get; init; }
  public string? Model { get; init; }
  public required string ProviderId { get; init; }
  public required bool AlreadyAdded { get; init; }
}
