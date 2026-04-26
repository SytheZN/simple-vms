namespace Shared.Models;

public interface IDataStreamAnalyzer
{
  string AnalyzerId { get; }
  IReadOnlyList<string> SupportedCodecs { get; }
}

public interface IDataStreamAnalyzerStreamOutput
{
  IReadOnlyList<DerivedStreamSpec> GetDerivedStreams(Guid cameraId);
  Task<OneOf<IDataStream, Error>> StartStreamAsync(Guid cameraId, string parentProfile, CancellationToken ct);
}

public interface IDataStreamAnalyzerEventOutput
{
  Task<OneOf<Success, Error>> StartEventsAsync(Guid cameraId, string parentProfile, CancellationToken ct);
  Task<OneOf<Success, Error>> StopEventsAsync(Guid cameraId, string parentProfile, CancellationToken ct);
}

public sealed record DerivedStreamSpec
{
  public required string ParentProfile { get; init; }
  public required string Profile { get; init; }
  public required StreamKind Kind { get; init; }
  public required string FormatId { get; init; }
  public string? Codec { get; init; }
}
