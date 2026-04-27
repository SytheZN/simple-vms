namespace Shared.Models;

public sealed class CameraConfiguration
{
  public required string Address { get; init; }
  public required string Name { get; init; }
  public required IReadOnlyList<SourceStreamSpec> Streams { get; init; }
  public required string[] Capabilities { get; init; }
  public Dictionary<string, string> Config { get; init; } = [];
  public Credentials? Credentials { get; init; }
}
