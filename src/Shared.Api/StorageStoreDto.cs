namespace Shared.Api;

public sealed class StorageStoreDto
{
  public required long TotalBytes { get; init; }
  public required long UsedBytes { get; init; }
  public required long FreeBytes { get; init; }
  public required long RecordingBytes { get; init; }
  public IReadOnlyList<StorageBreakdownDto>? Breakdown { get; init; }
}
