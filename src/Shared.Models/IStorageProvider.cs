namespace Shared.Models;

public interface IStorageProvider
{
  string ProviderId { get; }
  Task<ISegmentHandle> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct);
  Task<Stream> OpenReadAsync(string segmentRef, CancellationToken ct);
  Task PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct);
  Task<StorageStats> GetStatsAsync(CancellationToken ct);
}

public interface ISegmentHandle : IAsyncDisposable
{
  string SegmentRef { get; }
  Stream Stream { get; }
  Task FinalizeAsync(CancellationToken ct);
}

public sealed class SegmentMetadata
{
  public required Guid CameraId { get; init; }
  public required string Profile { get; init; }
  public required ulong StartTime { get; init; }
  public required string Codec { get; init; }
}

public sealed class StorageStats
{
  public required long TotalBytes { get; init; }
  public required long UsedBytes { get; init; }
  public required long FreeBytes { get; init; }
  public required long RecordingBytes { get; init; }
}
