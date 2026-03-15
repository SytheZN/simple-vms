namespace Shared.Models;

public sealed class CameraInfo
{
  public required Guid Id { get; init; }
  public required string Name { get; init; }
  public required string Address { get; init; }
  public required string ProviderId { get; init; }
  public required IReadOnlyList<StreamProfile> Streams { get; init; }
  public required string[] Capabilities { get; init; }
}
