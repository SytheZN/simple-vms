namespace Shared.Models;

public interface IStreamTap
{
  Task<OneOf<IDataStream, Error>> TapAsync(Guid cameraId, string profile, CancellationToken ct);
}
