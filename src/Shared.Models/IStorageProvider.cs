namespace Shared.Models;

public interface IStorageProvider
{
  string ProviderId { get; }
  Task<OneOf<ISegmentHandle, Error>> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct);
  Task<OneOf<Stream, Error>> OpenReadAsync(string segmentRef, CancellationToken ct);
  Task<OneOf<Success, Error>> PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct);
  Task<OneOf<StorageStats, Error>> GetStatsAsync(CancellationToken ct);
  Task<OneOf<long, Error>> GetFreeBytesAsync(CancellationToken ct);
}
