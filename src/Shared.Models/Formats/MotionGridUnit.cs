namespace Shared.Models.Formats;

public sealed class MotionGridUnit : IDataUnit
{
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ulong Timestamp { get; init; }
  public required bool IsSyncPoint { get; init; }
  public bool IsHeader => false;
  public required ushort Width { get; init; }
  public required ushort Height { get; init; }
}
