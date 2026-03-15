namespace Shared.Models;

public interface IKeyframeRepository
{
  Task<OneOf<IReadOnlyList<Keyframe>, Error>> GetBySegmentIdAsync(Guid segmentId, CancellationToken ct = default);
  Task<OneOf<Keyframe, Error>> GetNearestAsync(Guid segmentId, ulong timestamp, CancellationToken ct = default);
  Task<OneOf<Success, Error>> CreateBatchAsync(IReadOnlyList<Keyframe> keyframes, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteBySegmentIdsAsync(IReadOnlyList<Guid> segmentIds, CancellationToken ct = default);
}
