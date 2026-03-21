using Shared.Models;

namespace Capture.Rtsp;

public sealed partial class RtspPlugin : IPlugin
{
  public PluginMetadata Metadata { get; } = new()
  {
    Id = "rtsp",
    Name = "RTSP Capture",
    Version = "1.0.0",
    Description = "RTSP/TCP interleaved capture source"
  };

  public OneOf<Success, Error> Initialize(PluginContext context) =>
    new Success();

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
