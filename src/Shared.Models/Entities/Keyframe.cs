namespace Shared.Models.Entities;

public sealed class Keyframe
{
  public required Guid SegmentId { get; set; }
  public required ulong Timestamp { get; set; }
  public required long ByteOffset { get; set; }
}
