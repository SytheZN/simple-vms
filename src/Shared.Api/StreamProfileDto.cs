using Shared.Models;

namespace Shared.Api;

public sealed class StreamProfileDto
{
  public required string Profile { get; init; }
  public required StreamKind Kind { get; init; }
  public required string Codec { get; init; }
  public required string Resolution { get; init; }
  public required decimal Fps { get; init; }
  public required bool RecordingEnabled { get; init; }
}
