namespace Shared.Models;

public interface IStreamTap
{
  IAsyncEnumerable<NalUnit> TapAsync(Guid cameraId, string profile, CancellationToken ct);
}
