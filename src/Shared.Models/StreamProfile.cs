namespace Shared.Models;

public sealed class StreamProfile
{
  public required Guid Id { get; init; }
  public required string Profile { get; init; }
  public required StreamKind Kind { get; init; }
  public required string FormatId { get; init; }
  public string? Codec { get; init; }
  public string? Resolution { get; init; }
  public decimal? Fps { get; init; }
  public int? Bitrate { get; init; }
  public required string Uri { get; init; }
  public bool IsRootStream { get; init; }
}
