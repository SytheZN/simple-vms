namespace Shared.Models;

public sealed class CameraConfiguration
{
  public required string Address { get; init; }
  public required string Name { get; init; }
  public required IReadOnlyList<StreamProfile> Streams { get; init; }
  public required string[] Capabilities { get; init; }
  public Dictionary<string, string> Config { get; init; } = [];
}

public sealed class StreamProfile
{
  public required string Profile { get; init; }
  public required StreamKind Kind { get; init; }
  public required string FormatId { get; init; }
  public string? Codec { get; init; }
  public string? Resolution { get; init; }
  public int? Fps { get; init; }
  public int? Bitrate { get; init; }
  public required string Uri { get; init; }
}
