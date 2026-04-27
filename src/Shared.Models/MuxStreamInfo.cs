namespace Shared.Models;

public sealed class MuxStreamInfo
{
  public required string DataFormat { get; init; }
  public required string MimeType { get; init; }
  public required string FileExtension { get; init; }
  public required string Resolution { get; init; }
  public required int Fps { get; init; }
}
