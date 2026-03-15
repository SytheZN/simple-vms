namespace Shared.Models;

public sealed class SegmentInfo
{
  public required Guid Id { get; init; }
  public required ulong StartTime { get; init; }
  public required ulong EndTime { get; init; }
  public required string SegmentRef { get; init; }
  public required long SizeBytes { get; init; }
}
