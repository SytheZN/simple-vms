namespace Shared.Models;

public sealed class Segment
{
  public required Guid Id { get; set; }
  public required Guid StreamId { get; set; }
  public required ulong StartTime { get; set; }
  public required ulong EndTime { get; set; }
  public required string SegmentRef { get; set; }
  public required long SizeBytes { get; set; }
  public required int KeyframeCount { get; set; }
}
