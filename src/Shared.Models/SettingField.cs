namespace Shared.Models;

public record SettingField
{
  public required string Key { get; init; }
  public required int Order { get; init; }
  public required string Label { get; init; }
  public required string Type { get; init; }
  public string? Description { get; init; }
  public string? DefaultValue { get; init; }
  public bool Required { get; init; }
  public IReadOnlyList<SettingFieldOption>? Options { get; init; }
}
