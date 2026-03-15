namespace Shared.Models;

public interface ICameraRegistry
{
  Task<IReadOnlyList<CameraInfo>> GetCamerasAsync(CancellationToken ct);
  Task<CameraInfo?> GetCameraAsync(Guid cameraId, CancellationToken ct);
}
