namespace Shared.Models;

public sealed class NalUnit
{
  public required NalUnitType Type { get; init; }
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ulong Timestamp { get; init; }
  public required bool IsKeyframe { get; init; }
}

public enum NalUnitType
{
  Slice,
  Idr,
  Sps,
  Pps,
  Vps,
  Sei,
  Other
}
