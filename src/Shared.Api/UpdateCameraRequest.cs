namespace Shared.Api;

public sealed class UpdateCameraRequest
{
  public string? Name { get; init; }
  public string? Address { get; init; }
  public string? ProviderId { get; init; }
  public CredentialsDto? Credentials { get; init; }
  public int? RtspPortOverride { get; init; }
}
