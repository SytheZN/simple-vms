namespace Shared.Api;

public sealed class RetentionPolicy
{
  public required string Mode { get; init; }
  public required long Value { get; init; }
}
