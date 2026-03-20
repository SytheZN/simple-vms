namespace Shared.Models;

public interface IDataUnit
{
  ReadOnlyMemory<byte> Data { get; }
  ulong Timestamp { get; }
  bool IsSyncPoint { get; }
}
