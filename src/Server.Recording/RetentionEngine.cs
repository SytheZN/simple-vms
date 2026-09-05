using System.Globalization;
using Microsoft.Extensions.Logging;
using Server.Core;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Entities;

namespace Server.Recording;

public sealed class RetentionEngine : IAsyncDisposable
{
  private const int TickIntervalMinutes = 1;
  private const int FullEvaluationEveryTicks = 15;
  private const long GbBytes = 1024L * 1024L * 1024L;
  private const long HardFloorBytes = (long)(0.2 * GbBytes);
  private const int PurgeChunkSize = 200;
  private const int MaxPurgeChunks = 20;
  private const string GlobalModeKey = "retention.mode";
  private const string GlobalValueKey = "retention.value";
  private const string MinFreeSpaceGbKey = "retention.minFreeSpaceGb";
  private const decimal MinFreeSpaceGbFloor = 0.5m;
  private const decimal MinFreeSpaceGbDefault = 2.0m;
  private const string SystemEventDaysKey = "retention.systemEventDays";
  private const int DefaultSystemEventDays = 180;

  private readonly IPluginHost _plugins;
  private readonly IRecordingController _recording;
  private readonly ILogger _logger;
  private CancellationTokenSource? _cts;
  private Task? _loop;
  private bool _disposed;

  public RetentionEngine(IPluginHost plugins, IRecordingController recording, ILogger logger)
  {
    _plugins = plugins;
    _recording = recording;
    _logger = logger;
  }

  public void Start(CancellationToken ct)
  {
    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _loop = RunLoopAsync(_cts.Token);
  }

  private async Task RunLoopAsync(CancellationToken ct)
  {
    var tick = 0;
    while (!ct.IsCancellationRequested)
    {
      try
      {
        await GuardFreeSpaceAsync(ct);
        if (tick % FullEvaluationEveryTicks == 0)
          await EvaluateAsync(ct);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Retention loop iteration failed");
      }

      tick++;
      await Task.Delay(TimeSpan.FromMinutes(TickIntervalMinutes), ct)
        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
      if (ct.IsCancellationRequested) break;
    }
  }

  internal async Task GuardFreeSpaceAsync(CancellationToken ct)
  {
    var data = _plugins.DataProvider;
    var storage = _plugins.StorageProviders.FirstOrDefault();
    if (storage == null) return;

    var minGb = await ReadMinFreeSpaceGbAsync(ct);
    var minBytes = (long)(minGb * GbBytes);

    var stats = await storage.GetStatsAsync(ct);
    var freeBefore = stats.FreeBytes;

    if (freeBefore < HardFloorBytes && !_recording.IsHalted)
    {
      var haltedCount = _recording.WriterCount;
      _logger.LogCritical(
        "Free space {FreeBytes} bytes below hard floor {Floor}; halting all recording",
        freeBefore, HardFloorBytes);
      await LogSystemEventAsync(
        SystemEventFactory.RetentionEmergencyStop(freeBefore, HardFloorBytes, haltedCount, NowMicros()),
        ct);
      await _recording.HaltAllAsync();
    }

    if (freeBefore >= minBytes)
    {
      if (_recording.IsHalted && freeBefore >= minBytes)
      {
        _logger.LogInformation(
          "Free space {FreeBytes} bytes above trim threshold {Min}; resuming recording",
          freeBefore, minBytes);
        await LogSystemEventAsync(
          SystemEventFactory.RetentionRecordingResumed(freeBefore, minBytes, NowMicros()),
          ct);
        await _recording.ResumeAsync(ct);
      }
      return;
    }

    _logger.LogWarning(
      "Free space {FreeBytes} bytes below minimum {Min}; trimming oldest segments",
      freeBefore, minBytes);

    var purgedSegments = 0;
    var purgedBytes = 0L;
    var freeAfter = freeBefore;

    for (var i = 0; i < MaxPurgeChunks; i++)
    {
      ct.ThrowIfCancellationRequested();
      var batchResult = await data.Segments.GetOldestAcrossStreamsAsync(PurgeChunkSize, ct);
      if (batchResult.IsT1 || batchResult.AsT0.Count == 0) break;

      var batch = batchResult.AsT0.ToList();
      await PurgeSegmentsAsync(data, storage, batch, ct);
      purgedSegments += batch.Count;
      purgedBytes += batch.Sum(s => s.SizeBytes);

      var chunkStats = await storage.GetStatsAsync(ct);
      freeAfter = chunkStats.FreeBytes;
      if (freeAfter >= minBytes) break;
    }

    await LogSystemEventAsync(
      SystemEventFactory.RetentionLowSpacePurge(
        freeBefore, freeAfter, minBytes, purgedSegments, purgedBytes, NowMicros()),
      ct);

    if (freeAfter < minBytes)
      _logger.LogWarning(
        "Free-space guard exhausted purge cap: still {Free} bytes free after removing {Segments} segments ({Bytes} bytes)",
        freeAfter, purgedSegments, purgedBytes);

    if (_recording.IsHalted && freeAfter >= minBytes)
    {
      await LogSystemEventAsync(
        SystemEventFactory.RetentionRecordingResumed(freeAfter, minBytes, NowMicros()),
        ct);
      await _recording.ResumeAsync(ct);
    }
  }

  private async Task<decimal> ReadMinFreeSpaceGbAsync(CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Config.GetAsync("server", MinFreeSpaceGbKey, ct);
    if (result.IsT0
        && result.AsT0 != null
        && decimal.TryParse(result.AsT0, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
        && v >= MinFreeSpaceGbFloor)
      return v;
    return MinFreeSpaceGbDefault;
  }

  private async Task LogSystemEventAsync(SystemEvent evt, CancellationToken ct)
  {
    var result = await _plugins.DataProvider.SystemEvents.CreateAsync(evt, ct);
    if (result.IsT1)
      _logger.LogWarning("Failed to persist system event {Type}: {Message}",
        evt.Type, result.AsT1.Message);
  }

  private static ulong NowMicros() =>
    DateTimeOffset.UtcNow.ToUnixMicroseconds();

  internal async Task EvaluateAsync(CancellationToken ct)
  {
    var data = _plugins.DataProvider;
    var storage = _plugins.StorageProviders.FirstOrDefault();
    if (storage == null)
      return;

    var globalPolicy = await GetGlobalPolicyAsync(ct);
    StorageStats? storageStats = null;

    await PurgeSystemEventsAsync(data, ct);

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
        if (stream.DeletedAt != null)
          continue;
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

      await PurgeEventsAsync(data, camera, streamsResult.AsT0, ct);

      foreach (var stream in streamsResult.AsT0)
      {
        if (stream.DeletedAt == null)
          continue;

        var oldestResult = await data.Segments.GetOldestAsync(stream.Id, 1, ct);
        if (oldestResult.IsT1 || oldestResult.AsT0.Count > 0)
          continue;

        var deleteResult = await data.Streams.DeleteAsync(stream.Id, ct);
        if (deleteResult.IsT1)
        {
          _logger.LogWarning("Retention: failed to hard-delete soft-deleted stream {StreamId}: {Message}",
            stream.Id, deleteResult.AsT1.Message);
          continue;
        }

        foreach (var entry in _plugins.Plugins)
        {
          if (entry.Plugin is IPluginStreamSettings settings)
          {
            var cleanup = await settings.OnRemovedAsync(stream.Id, ct);
            if (cleanup.IsT1)
              _logger.LogWarning("Retention: plugin {Plugin} OnRemovedAsync failed for stream {Stream}: {Error}",
                entry.Metadata.Id, stream.Id, cleanup.AsT1.Message);
          }
        }

        _logger.LogInformation("Retention: hard-deleted soft-deleted stream {StreamId} (camera {CameraId}, profile '{Profile}')",
          stream.Id, stream.CameraId, stream.Profile);
      }
    }

    _logger.LogDebug("Retention evaluation complete");
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

  private async Task PurgeEventsAsync(
    IDataProvider data, Camera camera, IReadOnlyList<CameraStream> streams, CancellationToken ct)
  {
    ulong? cutoff = null;

    foreach (var stream in streams)
    {
      var oldestResult = await data.Segments.GetOldestAsync(stream.Id, 1, ct);
      if (oldestResult.IsT1 || oldestResult.AsT0.Count == 0)
        continue;

      var start = oldestResult.AsT0[0].StartTime;
      if (cutoff == null || start < cutoff)
        cutoff = start;
    }

    if (cutoff == null)
      return;

    var deleteResult = await data.Events.DeleteOlderThanAsync(camera.Id, cutoff.Value, ct);
    if (deleteResult.IsT1)
    {
      _logger.LogWarning("Retention: failed to purge events for camera {CameraId}: {Message}",
        camera.Id, deleteResult.AsT1.Message);
      return;
    }

    if (deleteResult.AsT0 > 0)
      _logger.LogInformation("Purged {Count} events for camera {CameraId}",
        deleteResult.AsT0, camera.Id);
  }

  private async Task PurgeSystemEventsAsync(IDataProvider data, CancellationToken ct)
  {
    var daysResult = await data.Config.GetAsync("server", SystemEventDaysKey, ct);
    var days = daysResult.IsT0 && int.TryParse(daysResult.AsT0, out var d) && d > 0
      ? d
      : DefaultSystemEventDays;

    var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixMicroseconds();
    var result = await data.SystemEvents.DeleteOlderThanAsync(cutoff, ct);
    if (result.IsT1)
      _logger.LogWarning("Retention: failed to purge system events: {Message}", result.AsT1.Message);
    else if (result.AsT0 > 0)
      _logger.LogInformation("Retention: purged {Count} system event(s) older than {Days} days",
        result.AsT0, days);
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
