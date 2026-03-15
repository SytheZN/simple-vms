namespace Shared.Models;

public interface IEventRepository
{
  Task<IReadOnlyList<CameraEvent>> QueryAsync(Guid? cameraId, string? type, ulong from, ulong to, int limit, int offset, CancellationToken ct = default);
  Task<CameraEvent?> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task CreateAsync(CameraEvent evt, CancellationToken ct = default);
  Task<IReadOnlyList<CameraEvent>> GetByTimeRangeAsync(Guid cameraId, ulong from, ulong to, CancellationToken ct = default);
}
