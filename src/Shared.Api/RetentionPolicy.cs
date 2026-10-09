namespace Shared.Api;

public sealed class RetentionPolicy
{
  public required string Mode { get; init; }
  public required decimal Value { get; init; }
  public required decimal MinFreeSpaceGb { get; init; }
}
