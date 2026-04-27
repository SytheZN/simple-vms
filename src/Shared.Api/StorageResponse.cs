namespace Shared.Api;

public sealed class StorageResponse
{
  public required IReadOnlyList<StorageStoreDto> Stores { get; init; }
}
