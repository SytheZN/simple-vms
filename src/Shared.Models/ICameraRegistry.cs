namespace Shared.Models;

public interface ICameraRegistry
{
  Task<OneOf<IReadOnlyList<CameraInfo>, Error>> GetCamerasAsync(CancellationToken ct);
  Task<OneOf<CameraInfo, Error>> GetCameraAsync(Guid cameraId, CancellationToken ct);
}
