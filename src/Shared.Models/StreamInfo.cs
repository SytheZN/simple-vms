namespace Shared.Models;

public sealed class StreamInfo
{
  public required CodecInfo Codec { get; init; }
  public string? Resolution { get; init; }
  public int? Fps { get; init; }
}
