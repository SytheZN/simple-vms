namespace Shared.Models;

public sealed class Fragment
{
  public required ulong Timestamp { get; init; }
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required bool IsKeyframe { get; init; }
}
