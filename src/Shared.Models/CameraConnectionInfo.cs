namespace Shared.Models;

public sealed class CameraConnectionInfo
{
  public required string Uri { get; init; }
  public string? Username { get; init; }
  public string? Password { get; init; }
}
