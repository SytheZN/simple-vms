using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;

namespace Server.Recording;

public sealed class RetentionEngine : IAsyncDisposable
{
  private const int DefaultIntervalMinutes = 15;
  private const string GlobalModeKey = "retention.mode";
  private const string GlobalValueKey = "retention.value";

  private readonly IPluginHost _plugins;
  private readonly ILogger _logger;
  private CancellationTokenSource? _cts;
  private Task? _loop;
  private bool _disposed;

  public RetentionEngine(IPluginHost plugins, ILogger logger)
  {
    _plugins = plugins;
    _logger = logger;
  }

  public void Start(CancellationToken ct)
  {
    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _loop = RunLoopAsync(_cts.Token);
  }

  private async Task RunLoopAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      try
      {
        await Task.Delay(TimeSpan.FromMinutes(DefaultIntervalMinutes), ct);
      }
      catch (OperationCanceledException)
      {
        break;
      }

      try
      {
        await EvaluateAsync(ct);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Retention evaluation failed");
      }
    }
  }

  internal async Task EvaluateAsync(CancellationToken ct)
  {
    var data = _plugins.DataProvider;
    var storage = _plugins.StorageProviders.FirstOrDefault();
    if (storage == null)
      return;

    var globalPolicy = await GetGlobalPolicyAsync(ct);
    StorageStats? storageStats = null;

    var camerasResult = await data.Cameras.GetAllAsync(ct);
    if (camerasResult.IsT1)
    {
      _logger.LogError("Retention: failed to load cameras: {Message}", camerasResult.AsT1.Message);
      return;
    }

    foreach (var camera in camerasResult.AsT0)
    {
      var streamsResult = await data.Streams.GetByCameraIdAsync(camera.Id, ct);
      if (streamsResult.IsT1)
        continue;

      foreach (var stream in streamsResult.AsT0)
      {
        if (stream.Kind != StreamKind.Quality)
          continue;
        if (!stream.RecordingEnabled)
          continue;

        var (mode, value) = ResolvePolicy(stream, camera, globalPolicy);
        if (mode == RetentionMode.Default)
          continue;

        switch (mode)
        {
          case RetentionMode.Days:
            await PurgeByDaysAsync(data, storage, stream.Id, value, ct);
            break;

          case RetentionMode.Bytes:
            await PurgeByBytesAsync(data, storage, stream.Id, value, ct);
            break;

          case RetentionMode.Percent:
            storageStats ??= await storage.GetStatsAsync(ct);
            if (storageStats.TotalBytes > 0)
              await PurgeByPercentAsync(data, storage, stream.Id, value, storageStats, ct);
            break;
        }
      }
    }

    foreach (var camera in camerasResult.AsT0)
    {
      var streamsResult = await data.Streams.GetByCameraIdAsync(camera.Id, ct);
      if (streamsResult.IsT1)
        continue;

      var streams = streamsResult.AsT0;
      var qualityStreams = streams.Where(s => s.Kind == StreamKind.Quality).ToList();
      var metadataStreams = streams.Where(s => s.Kind == StreamKind.Metadata).ToList();

      if (qualityStreams.Count == 0 || metadataStreams.Count == 0)
        continue;

      foreach (var metaStream in metadataStreams)
        await PurgeOrphanedMetadataAsync(data, storage, metaStream.Id, qualityStreams, ct);
    }

    _logger.LogDebug("Retention evaluation complete");
  }

  private async Task PurgeOrphanedMetadataAsync(
    IDataProvider data, IStorageProvider storage, Guid metadataStreamId,
    List<CameraStream> qualityStreams, CancellationToken ct)
  {
    var metaSegmentsResult = await data.Segments.GetByTimeRangeAsync(
      metadataStreamId, 0, ulong.MaxValue, ct);
    if (metaSegmentsResult.IsT1)
      return;

    var toPurge = new List<Segment>();

    foreach (var metaSeg in metaSegmentsResult.AsT0)
    {
      var hasOverlap = false;

      foreach (var qualityStream in qualityStreams)
      {
        var overlapResult = await data.Segments.GetByTimeRangeAsync(
          qualityStream.Id, metaSeg.StartTime, metaSeg.EndTime, ct);
        if (overlapResult.IsT0 && overlapResult.AsT0.Count > 0)
        {
          hasOverlap = true;
          break;
        }
      }

      if (!hasOverlap)
        toPurge.Add(metaSeg);
    }

    if (toPurge.Count > 0)
    {
      await PurgeSegmentsAsync(data, storage, toPurge, ct);
      _logger.LogInformation(
        "Purged {Count} orphaned metadata segments for stream {StreamId}",
        toPurge.Count, metadataStreamId);
    }
  }

  internal static (RetentionMode Mode, long Value) ResolvePolicy(
    CameraStream stream, Camera camera, (RetentionMode Mode, long Value) global)
  {
    if (stream.RetentionMode != RetentionMode.Default)
      return (stream.RetentionMode, stream.RetentionValue);

    if (camera.RetentionMode != RetentionMode.Default)
      return (camera.RetentionMode, camera.RetentionValue);

    return global;
  }

  private async Task PurgeByDaysAsync(
    IDataProvider data, IStorageProvider storage, Guid streamId, long days, CancellationToken ct)
  {
    var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixMicroseconds();
    var segmentsResult = await data.Segments.GetOldestAsync(streamId, int.MaxValue, ct);
    if (segmentsResult.IsT1)
      return;

    var toPurge = segmentsResult.AsT0.Where(s => s.EndTime < cutoff).ToList();
    if (toPurge.Count > 0)
      await PurgeSegmentsAsync(data, storage, toPurge, ct);
  }

  private async Task PurgeByBytesAsync(
    IDataProvider data, IStorageProvider storage, Guid streamId, long maxBytes, CancellationToken ct)
  {
    var totalResult = await data.Segments.GetTotalSizeAsync(streamId, ct);
    if (totalResult.IsT1)
      return;

    var total = totalResult.AsT0;
    if (total <= maxBytes)
      return;

    var segmentsResult = await data.Segments.GetOldestAsync(streamId, int.MaxValue, ct);
    if (segmentsResult.IsT1)
      return;

    var toPurge = new List<Segment>();
    foreach (var seg in segmentsResult.AsT0)
    {
      if (total <= maxBytes)
        break;
      toPurge.Add(seg);
      total -= seg.SizeBytes;
    }

    if (toPurge.Count > 0)
      await PurgeSegmentsAsync(data, storage, toPurge, ct);
  }

  private async Task PurgeByPercentAsync(
    IDataProvider data, IStorageProvider storage, Guid streamId, long maxPercent,
    StorageStats stats, CancellationToken ct)
  {
    if (stats.TotalBytes <= 0)
      return;

    var usedPercent = (long)(stats.UsedBytes * 100.0 / stats.TotalBytes);
    if (usedPercent <= maxPercent)
      return;

    var segmentsResult = await data.Segments.GetOldestAsync(streamId, int.MaxValue, ct);
    if (segmentsResult.IsT1)
      return;

    var bytesToFree = stats.UsedBytes - (long)(stats.TotalBytes * maxPercent / 100.0);
    var freed = 0L;
    var toPurge = new List<Segment>();

    foreach (var seg in segmentsResult.AsT0)
    {
      if (freed >= bytesToFree)
        break;
      toPurge.Add(seg);
      freed += seg.SizeBytes;
    }

    if (toPurge.Count > 0)
      await PurgeSegmentsAsync(data, storage, toPurge, ct);
  }

  private async Task PurgeSegmentsAsync(
    IDataProvider data, IStorageProvider storage, List<Segment> segments, CancellationToken ct)
  {
    var ids = segments.Select(s => s.Id).ToList();
    var refs = segments.Select(s => s.SegmentRef).ToList();

    await storage.PurgeAsync(refs, ct);
    await data.Keyframes.DeleteBySegmentIdsAsync(ids, ct);
    await data.Segments.DeleteBatchAsync(ids, ct);

    _logger.LogInformation("Purged {Count} segments ({Bytes} bytes)",
      segments.Count, segments.Sum(s => s.SizeBytes));
  }

  private async Task<(RetentionMode Mode, long Value)> GetGlobalPolicyAsync(CancellationToken ct)
  {
    var modeResult = await _plugins.DataProvider.Config.GetAsync("server", GlobalModeKey, ct);
    var valueResult = await _plugins.DataProvider.Config.GetAsync("server", GlobalValueKey, ct);

    var modeStr = modeResult.IsT0 ? modeResult.AsT0 ?? "days" : "days";
    var value = valueResult.IsT0 && long.TryParse(valueResult.AsT0, out var v) ? v : 30;

    var mode = modeStr switch
    {
      "bytes" => RetentionMode.Bytes,
      "percent" => RetentionMode.Percent,
      _ => RetentionMode.Days
    };

    return (mode, value);
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    _cts?.Cancel();
    if (_loop != null)
    {
      try { await _loop; }
      catch { }
    }
    _cts?.Dispose();
  }
}
