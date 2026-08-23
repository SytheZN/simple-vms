namespace Shared.Models;

public sealed record DerivedStreamSpec : StreamSpec
{
  public required string ParentProfile { get; init; }

  /// <summary>
  /// Clear this for streams the analyzer regenerates on demand, such as live previews, which the
  /// recorder would otherwise hold open for the lifetime of the parent recording.
  /// </summary>
  public bool Recordable { get; init; } = true;
}
