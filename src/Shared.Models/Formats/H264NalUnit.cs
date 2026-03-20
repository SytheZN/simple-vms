namespace Shared.Models.Formats;

public sealed class H264NalUnit : IDataUnit
{
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ulong Timestamp { get; init; }
  public required bool IsSyncPoint { get; init; }
  public required H264NalType NalType { get; init; }
}

public enum H264NalType
{
  Slice,
  Idr,
  Sps,
  Pps,
  Sei,
  Other
}
