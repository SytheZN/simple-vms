namespace Shared.Models;

public interface IRecordingAccess
{
  Task<IReadOnlyList<SegmentInfo>> QueryAsync(Guid cameraId, string profile, ulong from, ulong to, CancellationToken ct);
  Task<Stream> OpenSegmentAsync(string segmentRef, CancellationToken ct);
}
