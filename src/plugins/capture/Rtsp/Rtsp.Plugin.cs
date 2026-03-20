using Shared.Models;

namespace Capture.Rtsp;

public sealed class RtspPlugin : IPlugin, ICaptureSource
{
  public PluginMetadata Metadata { get; } = new()
  {
    Id = "rtsp",
    Name = "RTSP Capture",
    Version = "1.0.0",
    Description = "RTSP/TCP interleaved capture source"
  };

  public string Protocol => "rtsp";

  public OneOf<Success, Error> Initialize(PluginContext context) =>
    new Success();

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

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
