namespace Shared.Models;

public interface ISegmentRepository
{
  Task<OneOf<Segment, Error>> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<IReadOnlyList<Segment>, Error>> GetByTimeRangeAsync(Guid streamId, ulong from, ulong to, CancellationToken ct = default);
  Task<OneOf<PlaybackPoint, Error>> FindPlaybackPointAsync(Guid streamId, ulong timestamp, CancellationToken ct = default);
  Task<OneOf<IReadOnlyList<Segment>, Error>> GetOldestAsync(Guid streamId, int limit, CancellationToken ct = default);
  Task<OneOf<long, Error>> GetTotalSizeAsync(Guid streamId, CancellationToken ct = default);
  Task<OneOf<Success, Error>> CreateAsync(Segment segment, CancellationToken ct = default);
  Task<OneOf<Success, Error>> UpdateAsync(Segment segment, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteBatchAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
