namespace Shared.Models;

public interface IStorageProvider
{
  string ProviderId { get; }
  Task<ISegmentHandle> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct);
  Task<Stream> OpenReadAsync(string segmentRef, CancellationToken ct);
  Task PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct);
  Task<StorageStats> GetStatsAsync(CancellationToken ct);
}
