namespace Shared.Models;

public sealed record DerivedStreamSpec : StreamSpec
{
  public required string ParentProfile { get; init; }
}
