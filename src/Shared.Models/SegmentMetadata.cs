namespace Shared.Models;

public sealed class SegmentMetadata
{
  public required Guid CameraId { get; init; }
  public required string Profile { get; init; }
  public required ulong StartTime { get; init; }
  public required string Codec { get; init; }
  public required string FileExtension { get; init; }
}
