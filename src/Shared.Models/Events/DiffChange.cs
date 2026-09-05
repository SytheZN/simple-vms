namespace Shared.Models.Events;

public sealed record DiffChange
{
  public required DiffChangeType Type { get; init; }
  public string? OldValue { get; init; }
  public string? NewValue { get; init; }
}
