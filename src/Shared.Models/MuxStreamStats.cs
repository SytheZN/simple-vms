namespace Shared.Models;

public sealed class MuxStreamStats
{
  public required decimal Fps { get; init; }
  public required string Resolution { get; init; }
  public required int BitrateKbps { get; init; }
}
