using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Analyzer.MotionGridH26x;

public sealed partial class MotionGridH26xPlugin : IPlugin
{
  public const string AnalyzerIdValue = "motion-grid-h26x";

  private IConfig _config = null!;
  private IStreamTap _streamTap = null!;
  private ICameraRegistry _cameraRegistry = null!;
  private ILogger _logger = null!;
  private readonly ConcurrentDictionary<(Guid CameraId, string ParentProfile), IAsyncDisposable> _workers = new();
  private readonly ConcurrentDictionary<Guid, CameraFilterSettings> _cameraFilterSettings = new();

  private sealed class CameraFilterSettings
  {
    public volatile string Algorithm = null!;
    public volatile bool Deblock;
    public volatile bool Despeckle;
    public volatile int WindowFrames;
    public volatile bool Dirty;
  }

  public PluginMetadata Metadata { get; } = new()
  {
    Id = AnalyzerIdValue,
    Name = "Motion Grid",
    Version = "1.0.0",
    Description = "Per-block motion grid extractor for H.264 and H.265"
  };

  public OneOf<Success, Error> Initialize(PluginContext context)
  {
    _config = context.Config;
    _streamTap = context.StreamTap
      ?? throw new InvalidOperationException("MotionGridH26xPlugin requires IStreamTap");
    _cameraRegistry = context.CameraRegistry
      ?? throw new InvalidOperationException("MotionGridH26xPlugin requires ICameraRegistry");
    _logger = context.LoggerFactory.CreateLogger(AnalyzerIdValue);
    return new Success();
  }

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());

  public async Task<OneOf<Success, Error>> StopAsync(CancellationToken ct)
  {
    foreach (var worker in _workers.Values)
      await worker.DisposeAsync();
    _workers.Clear();
    return new Success();
  }
}
