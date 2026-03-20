namespace Shared.Models;

public sealed class CameraConnectionInfo
{
  public required string Uri { get; init; }
  public IReadOnlyDictionary<string, string>? Credentials { get; init; }
}
