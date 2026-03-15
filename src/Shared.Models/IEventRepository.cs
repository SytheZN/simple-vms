namespace Shared.Models;

public interface IEventRepository
{
  Task<OneOf<IReadOnlyList<CameraEvent>, Error>> QueryAsync(Guid? cameraId, string? type, ulong from, ulong to, int limit, int offset, CancellationToken ct = default);
  Task<OneOf<CameraEvent, Error>> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<Success, Error>> CreateAsync(CameraEvent evt, CancellationToken ct = default);
  Task<OneOf<IReadOnlyList<CameraEvent>, Error>> GetByTimeRangeAsync(Guid cameraId, ulong from, ulong to, CancellationToken ct = default);
}
