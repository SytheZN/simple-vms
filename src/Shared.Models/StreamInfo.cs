namespace Shared.Models;

public sealed class StreamInfo
{
  public required string DataFormat { get; init; }
  public object? FormatParameters { get; init; }
  public string? Resolution { get; init; }
  public int? Fps { get; init; }
}
