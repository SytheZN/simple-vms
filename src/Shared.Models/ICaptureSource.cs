namespace Shared.Models;

public interface ICaptureSource
{
  string Protocol { get; }
  Task<OneOf<IStreamConnection, Error>> ConnectAsync(CameraConnectionInfo info, CancellationToken ct);
}
