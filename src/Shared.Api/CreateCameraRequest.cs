namespace Shared.Api;

public sealed class CreateCameraRequest
{
  public required string Address { get; init; }
  public string? ProviderId { get; init; }
  public CredentialsDto? Credentials { get; init; }
  public string? Name { get; init; }
  public int? RtspPortOverride { get; init; }
}
