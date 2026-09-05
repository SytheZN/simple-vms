using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Analyzer.Thumbnail;

public sealed partial class ThumbnailPlugin : IPlugin
{
  public const string AnalyzerIdValue = "thumbnail";

  private IConfig _config = null!;
  private IStreamTap _streamTap = null!;
  private ICameraRegistry _cameraRegistry = null!;
  private ILogger _logger = null!;
  private ILogger _perfLogger = null!;
  private readonly ConcurrentDictionary<(Guid CameraId, string ParentProfile), ThumbnailWorker> _workers = new();

  private volatile int _cachedSize;
  private volatile int _cachedQuality;
  private volatile int _cachedInterval;

  public PluginMetadata Metadata { get; } = new()
  {
    Id = AnalyzerIdValue,
    Name = "Thumbnail",
    Version = "1.0.0",
    Description = "Decodes keyframes into JPEG previews"
  };

  public OneOf<Success, Error> Initialize(PluginContext context)
  {
    _config = context.Config;
    _streamTap = context.StreamTap
      ?? throw new InvalidOperationException("ThumbnailPlugin requires IStreamTap");
    _cameraRegistry = context.CameraRegistry
      ?? throw new InvalidOperationException("ThumbnailPlugin requires ICameraRegistry");
    _logger = context.LoggerFactory.CreateLogger(AnalyzerIdValue);
    _perfLogger = context.LoggerFactory.CreateLogger("perf");
    RefreshCachedSettings();
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
