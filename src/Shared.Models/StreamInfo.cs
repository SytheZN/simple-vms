namespace Shared.Models;

public sealed class StreamInfo
{
  public required string DataFormat { get; init; }
  public object? FormatParameters { get; init; }
  public int? Fps { get; init; }
}

public sealed class VideoStreamInfo
{
  public required string DataFormat { get; init; }
  public required string MimeType { get; init; }
  public required string Resolution { get; init; }
  public required int Fps { get; init; }
}
