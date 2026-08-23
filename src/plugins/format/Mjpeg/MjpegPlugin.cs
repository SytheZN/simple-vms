using Shared.Models;

namespace Format.Mjpeg;

public sealed partial class MjpegPlugin : IPlugin
{
  public PluginMetadata Metadata { get; } = new()
  {
    Id = "mjpeg",
    Name = "MJPEG",
    Version = "1.0.0",
    Description = "Timestamped JPEG frame container"
  };

  public OneOf<Success, Error> Initialize(PluginContext context) =>
    new Success();

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
