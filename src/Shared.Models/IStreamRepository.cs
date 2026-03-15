namespace Shared.Models;

public interface IStreamRepository
{
  Task<IReadOnlyList<CameraStream>> GetByCameraIdAsync(Guid cameraId, CancellationToken ct = default);
  Task<CameraStream?> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task UpsertAsync(CameraStream stream, CancellationToken ct = default);
  Task DeleteAsync(Guid id, CancellationToken ct = default);
}
