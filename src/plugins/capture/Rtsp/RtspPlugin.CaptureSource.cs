using Shared.Models;

namespace Capture.Rtsp;

public sealed partial class RtspPlugin : ICaptureSource
{
  public string Protocol => "rtsp";

  public async Task<OneOf<IStreamConnection, Error>> ConnectAsync(
    CameraConnectionInfo info, CancellationToken ct)
  {
    try
    {
      var connection = await RtspConnection.CreateAsync(
        info.Uri, info.Credentials, ct);
      return connection;
    }
    catch (Exception ex)
    {
      return Error.Create(
        ModuleIds.PluginRtspCapture, 0x0001,
        Result.InternalError,
        $"RTSP connection failed: {ex.Message}");
    }
  }
}
