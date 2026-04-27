namespace Shared.Models;

public record SettingFieldOption
{
  public required string Value { get; init; }
  public required string Label { get; init; }
}
