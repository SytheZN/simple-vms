namespace Shared.Models;

public interface IVideoAnalyzer
{
  string AnalyzerId { get; }
  IReadOnlyList<string> SupportedCodecs { get; }
  Task StartAsync(Guid cameraId, string profile, CancellationToken ct);
  Task StopAsync(Guid cameraId, string profile, CancellationToken ct);
}
