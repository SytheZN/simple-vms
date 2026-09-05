namespace Shared.Models;

public sealed class CameraConnectionInfo
{
  public required Guid CameraId { get; init; }
  public required string Uri { get; init; }
  public IReadOnlyDictionary<string, string>? Credentials { get; init; }
}
