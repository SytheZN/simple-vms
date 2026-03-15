namespace Shared.Models;

public interface IRecordingAccess
{
  Task<OneOf<IReadOnlyList<SegmentInfo>, Error>> QueryAsync(Guid cameraId, string profile, ulong from, ulong to, CancellationToken ct);
  Task<OneOf<Stream, Error>> OpenSegmentAsync(string segmentRef, CancellationToken ct);
}
