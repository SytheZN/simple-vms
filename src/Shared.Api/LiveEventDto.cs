namespace Shared.Api;

public sealed class LiveEventDto
{
  public required Guid Id { get; init; }
  public required Guid CameraId { get; init; }
  public required string Type { get; init; }
  public required ulong StartTime { get; init; }
  public Dictionary<string, string>? Metadata { get; init; }
  public bool Ended { get; init; }
}
