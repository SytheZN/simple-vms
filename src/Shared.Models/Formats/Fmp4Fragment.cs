namespace Shared.Models.Formats;

public sealed class Fmp4Fragment : IDataUnit
{
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ulong Timestamp { get; init; }
  public required ulong MediaTimestamp { get; init; }
  public required bool IsSyncPoint { get; init; }
  public required bool IsHeader { get; init; }
}
