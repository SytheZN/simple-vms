namespace Shared.Models;

public sealed class PlaybackPoint
{
  public required Guid SegmentId { get; init; }
  public required string SegmentRef { get; init; }
  public required ulong KeyframeTimestamp { get; init; }
  public required long ByteOffset { get; init; }
}
