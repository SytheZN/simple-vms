namespace Shared.Models.Formats;

public sealed class JpegUnit : IDataUnit
{
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ulong Timestamp { get; init; }
  public required ushort Width { get; init; }
  public required ushort Height { get; init; }
  public bool IsSyncPoint => true;
}
