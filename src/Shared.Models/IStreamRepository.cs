namespace Shared.Models;

public interface IStreamRepository
{
  Task<OneOf<IReadOnlyList<CameraStream>, Error>> GetByCameraIdAsync(Guid cameraId, CancellationToken ct = default);
  Task<OneOf<CameraStream, Error>> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<Success, Error>> UpsertAsync(CameraStream stream, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct = default);
}
