namespace Shared.Models;

public interface ISegmentRepository
{
  Task<IReadOnlyList<Segment>> GetByTimeRangeAsync(Guid streamId, ulong from, ulong to, CancellationToken ct = default);
  Task<IReadOnlyList<Segment>> GetOldestAsync(Guid streamId, int limit, CancellationToken ct = default);
  Task<long> GetTotalSizeAsync(Guid streamId, CancellationToken ct = default);
  Task CreateAsync(Segment segment, CancellationToken ct = default);
  Task DeleteBatchAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
