namespace Shared.Models;

public sealed class CameraStream
{
  public required Guid Id { get; set; }
  public required Guid CameraId { get; set; }
  public required string Profile { get; set; }
  public StreamKind Kind { get; set; } = StreamKind.Quality;
  public required string FormatId { get; set; }
  public string? Codec { get; set; }
  public string? Resolution { get; set; }
  public int? Fps { get; set; }
  public int? Bitrate { get; set; }
  public required string Uri { get; set; }
  public bool RecordingEnabled { get; set; }
}
