namespace Shared.Models.Formats;

public sealed class MotionGridFragment : IDataUnit
{
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ulong Timestamp { get; init; }
  public required bool IsSyncPoint { get; init; }
  public bool IsHeader => false;
}
