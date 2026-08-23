namespace Shared.Models.Formats;

public sealed class JpegFragment : IDataUnit
{
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ulong Timestamp { get; init; }
  public bool IsSyncPoint => true;
}
