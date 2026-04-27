namespace Shared.Api;

public sealed class CameraDto
{
  public required Guid Id { get; init; }
  public required string Name { get; init; }
  public required string Address { get; init; }
  public required string Status { get; init; }
  public required string ProviderId { get; init; }
  public required IReadOnlyList<StreamProfileDto> Streams { get; init; }
  public required string[] Capabilities { get; init; }
  public Dictionary<string, string>? Config { get; init; }
  public int? SegmentDuration { get; init; }
  public string? RetentionMode { get; init; }
  public long? RetentionValue { get; init; }
}
