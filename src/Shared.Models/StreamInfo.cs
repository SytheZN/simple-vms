namespace Shared.Models;

public sealed class StreamInfo
{
  public required string DataFormat { get; init; }
  public object? FormatParameters { get; init; }
  public decimal? Fps { get; init; }
}
