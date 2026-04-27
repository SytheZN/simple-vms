namespace Shared.Models;

public sealed record SourceStreamSpec : StreamSpec
{
  public required string Uri { get; init; }
}
