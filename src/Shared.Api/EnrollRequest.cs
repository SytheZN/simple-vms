namespace Shared.Api;

public sealed class EnrollRequest
{
  public required string Token { get; init; }
  public string? DeviceName { get; init; }
}
