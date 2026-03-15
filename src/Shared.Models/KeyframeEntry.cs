namespace Shared.Models;

public sealed class KeyframeEntry
{
  public required ulong Timestamp { get; init; }
  public required long ByteOffset { get; init; }
}
