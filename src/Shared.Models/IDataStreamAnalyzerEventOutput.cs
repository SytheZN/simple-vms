namespace Shared.Models;

public interface IDataStreamAnalyzerEventOutput
{
  Task<OneOf<Success, Error>> StartEventsAsync(Guid cameraId, string parentProfile, CancellationToken ct);
  Task<OneOf<Success, Error>> StopEventsAsync(Guid cameraId, string parentProfile, CancellationToken ct);
}
