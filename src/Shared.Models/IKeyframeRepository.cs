namespace Shared.Models;

public interface IKeyframeRepository
{
  Task<IReadOnlyList<Keyframe>> GetBySegmentIdAsync(Guid segmentId, CancellationToken ct = default);
  Task<Keyframe?> GetNearestAsync(Guid segmentId, ulong timestamp, CancellationToken ct = default);
  Task CreateBatchAsync(IReadOnlyList<Keyframe> keyframes, CancellationToken ct = default);
  Task DeleteBySegmentIdsAsync(IReadOnlyList<Guid> segmentIds, CancellationToken ct = default);
}
