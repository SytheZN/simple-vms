namespace Shared.Models;

public interface ISegmentReader : IAsyncDisposable
{
  Task<OneOf<Success, Error>> SeekAsync(long byteOffset, CancellationToken ct);
  IAsyncEnumerable<IDataUnit> ReadAsync(CancellationToken ct);
}
