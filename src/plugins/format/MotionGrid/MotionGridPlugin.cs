using Shared.Models;

namespace Format.MotionGrid;

public sealed partial class MotionGridPlugin : IPlugin
{
  public PluginMetadata Metadata { get; } = new()
  {
    Id = "motion-grid",
    Name = "Motion Grid",
    Version = "1.0.0",
    Description = "Per-cell activity grid container"
  };

  public OneOf<Success, Error> Initialize(PluginContext context) =>
    new Success();

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
