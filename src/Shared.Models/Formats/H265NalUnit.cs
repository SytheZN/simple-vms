namespace Shared.Models.Formats;

public sealed class H265NalUnit : IDataUnit
{
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ulong Timestamp { get; init; }
  public required bool IsSyncPoint { get; init; }
  public required H265NalType NalType { get; init; }
}

public enum H265NalType
{
  TrailN,
  TrailR,
  IdrWRadl,
  IdrNLp,
  Vps,
  Sps,
  Pps,
  Sei,
  Other
}
