using Shared.Models;

namespace Format.Fmp4;

public sealed partial class Fmp4H264Plugin : IPlugin
{
  public PluginMetadata Metadata { get; } = new()
  {
    Id = "fmp4-h264",
    Name = "Fragmented MP4 (H.264)",
    Version = "1.0.0",
    Description = "ISO BMFF fragmented MP4 muxer for H.264"
  };

  public OneOf<Success, Error> Initialize(PluginContext context) =>
    new Success();

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}

public sealed partial class Fmp4H265Plugin : IPlugin
{
  public PluginMetadata Metadata { get; } = new()
  {
    Id = "fmp4-h265",
    Name = "Fragmented MP4 (H.265)",
    Version = "1.0.0",
    Description = "ISO BMFF fragmented MP4 muxer for H.265"
  };

  public OneOf<Success, Error> Initialize(PluginContext context) =>
    new Success();

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
