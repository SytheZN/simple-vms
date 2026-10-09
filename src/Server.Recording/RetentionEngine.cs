using System.Globalization;
using Microsoft.Extensions.Logging;
using Server.Core;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Entities;

namespace Server.Recording;

public sealed class RetentionEngine : IAsyncDisposable
{
  private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
  private const int FreeSpaceCheckEveryTicks = 60;
  private const int PassEveryTicks = 900;
  private const long GbBytes = 1024L * 1024L * 1024L;
  private const long HardFloorBytes = (long)(0.2 * GbBytes);
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
  private long _minFreeBytes = (long)(MinFreeSpaceGbDefault * GbBytes);
  private bool _warnedUnknownSpace;
  private bool _disposed;

  internal Task Pass { get; private set; } = Task.CompletedTask;

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
    using var timer = new PeriodicTimer(TickInterval);
    var tick = 0;
    while (!ct.IsCancellationRequested)
    {
      try
      {
        var storage = _plugins.StorageProviders.FirstOrDefault();
        if (storage != null)
          await RunTickAsync(storage, tick, ct);
        tick++;
        await timer.WaitForNextTickAsync(ct);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Retention loop iteration failed");
      }
    }
  }

  private async Task RunTickAsync(IStorageProvider storage, int tick, CancellationToken ct)
  {
    await CheckEmergencyAsync(storage, ct);
    if (tick % FreeSpaceCheckEveryTicks == 0)
      await CheckFreeSpaceAsync(storage, ct);
    if (tick % PassEveryTicks == 0)
      DispatchPass(storage, lowSpaceFreeBefore: null, ct);
  }

  internal async Task CheckEmergencyAsync(IStorageProvider storage, CancellationToken ct)
  {
    if (await ReadFreeBytesAsync(storage, ct) is not { } free)
      return;

    if (free < 0)
    {
      WarnUnknownSpaceOnce();
      return;
    }

    if (free < HardFloorBytes && !_recording.IsHalted)
    {
      var haltedCount = _recording.WriterCount;
      _logger.LogCritical(
        "Free space {FreeBytes} bytes below hard floor {Floor}; halting all recording",
        free, HardFloorBytes);
      await _recording.HaltAllAsync();
      await LogSystemEventAsync(
        SystemEventFactory.RetentionEmergencyStop(free, HardFloorBytes, haltedCount, NowMicros()), ct);
    }
    else if (_recording.IsHalted && free >= _minFreeBytes)
    {
      _logger.LogInformation(
        "Free space {FreeBytes} bytes above minimum {Min}; resuming recording", free, _minFreeBytes);
      await LogSystemEventAsync(
        SystemEventFactory.RetentionRecordingResumed(free, _minFreeBytes, NowMicros()), ct);
      await _recording.ResumeAsync(ct);
    }
  }

  internal async Task CheckFreeSpaceAsync(IStorageProvider storage, CancellationToken ct)
  {
    _minFreeBytes = await ReadMinFreeBytesAsync(ct);

    if (await ReadFreeBytesAsync(storage, ct) is not { } free || free < 0 || free >= _minFreeBytes)
      return;

    _logger.LogWarning(
      "Free space {FreeBytes} bytes below minimum {Min}; running retention early", free, _minFreeBytes);
    DispatchPass(storage, lowSpaceFreeBefore: free, ct);
  }

  private void DispatchPass(IStorageProvider storage, long? lowSpaceFreeBefore, CancellationToken ct)
  {
    if (!Pass.IsCompleted)
      return;

    Pass = Task.Run(() => RunPassAsync(storage, lowSpaceFreeBefore, ct), ct);
  }

  private async Task RunPassAsync(IStorageProvider storage, long? lowSpaceFreeBefore, CancellationToken ct)
  {
    try
    {
      var tally = await EvaluateAsync(storage, ct);
      if (lowSpaceFreeBefore is not { } freeBefore)
        return;

      var freeAfter = await ReadFreeBytesAsync(storage, ct) ?? -1;
      await LogSystemEventAsync(
        SystemEventFactory.RetentionLowSpacePurge(
          freeBefore, freeAfter, _minFreeBytes, tally.Segments, tally.Bytes, NowMicros()),
        ct);
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Retention pass failed");
    }
  }

  private async Task<long?> ReadFreeBytesAsync(IStorageProvider storage, CancellationToken ct)
  {
    var result = await storage.GetFreeBytesAsync(ct);
    if (result.IsT0)
      return result.AsT0;

    _logger.LogWarning("Retention: failed to read free space: {Message}", result.AsT1.Message);
    return null;
  }

  private void WarnUnknownSpaceOnce()
  {
    if (_warnedUnknownSpace) return;
    _warnedUnknownSpace = true;
    _logger.LogWarning(
      "Storage cannot report free space; free space checks and the emergency stop are unavailable");
  }

  private async Task<long> ReadMinFreeBytesAsync(CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Config.GetAsync("server", MinFreeSpaceGbKey, ct);
    var gb = result.IsT0
        && result.AsT0 != null
        && decimal.TryParse(result.AsT0, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
        && v >= MinFreeSpaceGbFloor
      ? v
      : MinFreeSpaceGbDefault;
    return (long)(gb * GbBytes);
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

  internal sealed class PurgeTally
  {
    public int Segments { get; set; }
    public long Bytes { get; set; }
  }

  private sealed record QualityStream(
    CameraStream Stream, List<Segment> Segments,
    List<(CameraStream Stream, List<Segment> Segments)> Metadata);

  internal async Task<PurgeTally> EvaluateAsync(IStorageProvider storage, CancellationToken ct)
  {
    var data = _plugins.DataProvider;
    var tally = new PurgeTally();

    _minFreeBytes = await ReadMinFreeBytesAsync(ct);
    var systemEventCutoff = await PurgeSystemEventsAsync(data, ct);

    var camerasResult = await data.Cameras.GetAllAsync(ct);
    if (camerasResult.IsT1)
    {
      _logger.LogError("Retention: failed to load cameras: {Message}", camerasResult.AsT1.Message);
      return tally;
    }

    var globalPolicy = await GetGlobalPolicyAsync(ct);
    var streamsByCamera = new Dictionary<Guid, IReadOnlyList<CameraStream>>();
    var quality = new List<QualityStream>();
    var usages = new List<StreamUsage>();

    foreach (var camera in camerasResult.AsT0)
    {
      var streamsResult = await data.Streams.GetByCameraIdAsync(camera.Id, ct);
      if (streamsResult.IsT1)
        continue;
      streamsByCamera[camera.Id] = streamsResult.AsT0;

      var loaded = await LoadQualityStreamsAsync(data, streamsResult.AsT0, ct);
      foreach (var q in loaded)
      {
        var (mode, value) = ResolvePolicy(q.Stream, camera, globalPolicy);
        if (mode == RetentionMode.Default)
          continue;

        quality.Add(q);
        usages.Add(new StreamUsage(
          q.Stream.Id, mode, value,
          q.Segments.Sum(s => s.SizeBytes) + q.Metadata.Sum(m => m.Segments.Sum(s => s.SizeBytes)),
          RetentionQuotas.MeasureRate(q.Segments) + q.Metadata.Sum(m => RetentionQuotas.MeasureRate(m.Segments))));
      }
    }

    var usable = await UsableBytesAsync(storage, ct);
    var plan = RetentionQuotas.Compute(usages, usable);
    if (plan.PercentUnavailable)
      _logger.LogWarning("Retention: storage cannot report its size; percent retention is unavailable");

    var active = _recording.ActiveSegmentIds;
    var now = NowMicros();
    foreach (var q in quality)
    {
      var toPurge = SelectForTrim(
        q.Segments, q.Metadata.SelectMany(m => m.Segments).ToList(),
        plan.Allowances[q.Stream.Id], now, active);
      if (toPurge.Count > 0)
        await PurgeSegmentsAsync(data, storage, toPurge, tally, ct);
    }

    foreach (var (cameraId, streams) in streamsByCamera)
    {
      await PurgeUncoveredMetadataAsync(data, storage, streams, active, tally, ct);
      await PurgeEventsAsync(data, cameraId, streams, systemEventCutoff, ct);
      await HardDeleteEmptyStreamsAsync(data, streams, ct);
    }

    _logger.LogDebug("Retention evaluation complete");
    return tally;
  }

  private async Task<List<QualityStream>> LoadQualityStreamsAsync(
    IDataProvider data, IReadOnlyList<CameraStream> streams, CancellationToken ct)
  {
    var byId = streams.ToDictionary(s => s.Id);
    var result = new Dictionary<Guid, QualityStream>();

    foreach (var stream in streams.Where(s => s.Kind == StreamKind.Quality))
    {
      var segments = await data.Segments.GetOldestAsync(stream.Id, int.MaxValue, ct);
      if (segments.IsT1)
        continue;
      result[stream.Id] = new QualityStream(stream, segments.AsT0.ToList(), []);
    }

    foreach (var stream in streams.Where(s => s.Kind == StreamKind.Metadata))
    {
      var root = StreamHierarchy.ResolveRootStream(stream, id => byId.GetValueOrDefault(id), _logger);
      if (!result.TryGetValue(root.Id, out var owner))
        continue;

      var segments = await data.Segments.GetOldestAsync(stream.Id, int.MaxValue, ct);
      if (segments.IsT0)
        owner.Metadata.Add((stream, segments.AsT0.ToList()));
    }

    return result.Values.ToList();
  }

  private async Task<long?> UsableBytesAsync(IStorageProvider storage, CancellationToken ct)
  {
    var statsResult = await storage.GetStatsAsync(ct);
    if (statsResult.IsT1)
    {
      _logger.LogWarning("Retention: failed to read storage stats: {Message}", statsResult.AsT1.Message);
      return null;
    }

    var stats = statsResult.AsT0;
    if (stats.TotalBytes < 0 || stats.FreeBytes < 0)
      return null;
    return stats.FreeBytes + stats.RecordingBytes - _minFreeBytes;
  }

  internal static List<Segment> SelectForTrim(
    IReadOnlyList<Segment> quality, IReadOnlyList<Segment> metadata,
    StreamAllowance allowance, ulong now, IReadOnlySet<Guid> active)
  {
    var remaining = new Queue<Segment>(quality.OrderBy(s => s.StartTime));
    var metadataByEnd = metadata.OrderBy(s => s.EndTime).ToList();
    var metadataIndex = 0;
    var held = quality.Sum(s => s.SizeBytes) + metadata.Sum(s => s.SizeBytes);
    ulong? ageCutoff = allowance.MaxAge is { } age
      ? now - (ulong)Math.Min(age.TotalMicroseconds, now)
      : null;

    void DropUncoveredMetadata()
    {
      var coveredFrom = remaining.TryPeek(out var oldest) ? oldest.StartTime : ulong.MaxValue;
      while (metadataIndex < metadataByEnd.Count && metadataByEnd[metadataIndex].EndTime <= coveredFrom)
        held -= metadataByEnd[metadataIndex++].SizeBytes;
    }

    DropUncoveredMetadata();

    var purge = new List<Segment>();
    while (remaining.TryPeek(out var oldest) && !active.Contains(oldest.Id))
    {
      var expired = ageCutoff is { } cutoff && oldest.EndTime < cutoff;
      var over = allowance.MaxBytes is { } max && held > max;
      if (!expired && !over)
        break;

      purge.Add(remaining.Dequeue());
      held -= oldest.SizeBytes;
      DropUncoveredMetadata();
    }

    return purge;
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

  private async Task PurgeUncoveredMetadataAsync(
    IDataProvider data, IStorageProvider storage, IReadOnlyList<CameraStream> streams,
    IReadOnlySet<Guid> active, PurgeTally tally, CancellationToken ct)
  {
    var byId = streams.ToDictionary(s => s.Id);
    foreach (var metadata in streams.Where(s => s.Kind == StreamKind.Metadata))
    {
      var root = StreamHierarchy.ResolveRootStream(metadata, id => byId.GetValueOrDefault(id), _logger);

      var coveredFrom = ulong.MaxValue;
      if (root.Kind == StreamKind.Quality)
      {
        var rootOldest = await data.Segments.GetOldestAsync(root.Id, 1, ct);
        if (rootOldest.IsT1)
          continue;
        if (rootOldest.AsT0.Count > 0)
          coveredFrom = rootOldest.AsT0[0].StartTime;
      }

      var segmentsResult = await data.Segments.GetOldestAsync(metadata.Id, int.MaxValue, ct);
      if (segmentsResult.IsT1)
        continue;

      var toPurge = segmentsResult.AsT0
        .Where(s => s.EndTime <= coveredFrom && !active.Contains(s.Id))
        .ToList();
      if (toPurge.Count > 0)
        await PurgeSegmentsAsync(data, storage, toPurge, tally, ct);
    }
  }

  private async Task PurgeSegmentsAsync(
    IDataProvider data, IStorageProvider storage, List<Segment> segments, PurgeTally tally, CancellationToken ct)
  {
    var ids = segments.Select(s => s.Id).ToList();
    var refs = segments.Select(s => s.SegmentRef).ToList();

    var purged = await storage.PurgeAsync(refs, ct);
    if (purged.IsT1)
    {
      _logger.LogWarning("Retention: failed to purge {Count} segments: {Message}",
        segments.Count, purged.AsT1.Message);
      return;
    }

    await data.Keyframes.DeleteBySegmentIdsAsync(ids, ct);
    await data.Segments.DeleteBatchAsync(ids, ct);

    var bytes = segments.Sum(s => s.SizeBytes);
    tally.Segments += segments.Count;
    tally.Bytes += bytes;
    _logger.LogInformation("Purged {Count} segments ({Bytes} bytes)", segments.Count, bytes);
  }

  private async Task PurgeEventsAsync(
    IDataProvider data, Guid cameraId, IReadOnlyList<CameraStream> streams,
    ulong systemEventCutoff, CancellationToken ct)
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

    var deleteResult = await data.Events.DeleteOlderThanAsync(cameraId, cutoff ?? systemEventCutoff, ct);
    if (deleteResult.IsT1)
    {
      _logger.LogWarning("Retention: failed to purge events for camera {CameraId}: {Message}",
        cameraId, deleteResult.AsT1.Message);
      return;
    }

    if (deleteResult.AsT0 > 0)
      _logger.LogInformation("Purged {Count} events for camera {CameraId}",
        deleteResult.AsT0, cameraId);
  }

  private async Task HardDeleteEmptyStreamsAsync(
    IDataProvider data, IReadOnlyList<CameraStream> streams, CancellationToken ct)
  {
    foreach (var stream in streams.Where(s => s.DeletedAt != null))
    {
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

  private async Task<ulong> PurgeSystemEventsAsync(IDataProvider data, CancellationToken ct)
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

    return cutoff;
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
    try { await Pass; }
    catch { }
    _cts?.Dispose();
  }
}
