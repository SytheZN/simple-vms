namespace Shared.Models;

public record SettingGroup
{
  public required string Key { get; init; }
  public required int Order { get; init; }
  public required string Label { get; init; }
  public string? Description { get; init; }
  public required IReadOnlyList<SettingField> Fields { get; init; }
}
