namespace Shared.Api;

public sealed class ProbeResponse
{
  public required string Name { get; init; }
  public required IReadOnlyList<StreamProfileDto> Streams { get; init; }
  public required string[] Capabilities { get; init; }
  public required Dictionary<string, string> Config { get; init; }
}
