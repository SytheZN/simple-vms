namespace Shared.Api;

public sealed class ValidateFieldRequest
{
  public required string Key { get; init; }
  public required string Value { get; init; }
}
