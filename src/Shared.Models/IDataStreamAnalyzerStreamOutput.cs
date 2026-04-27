namespace Shared.Models;

public interface IDataStreamAnalyzerStreamOutput
{
  IReadOnlyList<DerivedStreamSpec> GetDerivedStreams(Guid cameraId);
  Task<OneOf<IDataStream, Error>> StartStreamAsync(Guid cameraId, string parentProfile, CancellationToken ct);
}
