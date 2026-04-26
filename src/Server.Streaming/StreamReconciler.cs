using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;

namespace Server.Streaming;

public sealed class StreamReconciler
{
  private readonly IPluginHost _plugins;
  private readonly ILogger _logger;

  public StreamReconciler(IPluginHost plugins, ILogger logger)
  {
    _plugins = plugins;
    _logger = logger;
  }

  public async Task<OneOf<Success, Error>> ReconcileCameraAsync(Guid cameraId, CancellationToken ct)
  {
    var streamsResult = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return streamsResult.AsT1;
    var allStreams = streamsResult.AsT0;
    var byProfileForLookup = allStreams
      .Where(s => s.DeletedAt == null && s.ProducerId == null)
      .ToDictionary(s => s.Profile);

    foreach (var identity in _plugins.Analyzers)
    {
      if (identity is not IDataStreamAnalyzerStreamOutput analyzer) continue;
      var producerId = identity.AnalyzerId;
      var specs = analyzer.GetDerivedStreams(cameraId);

      var existingForProducer = allStreams
        .Where(s => s.ProducerId == producerId)
        .ToDictionary(s => s.Profile);

      foreach (var spec in specs)
      {
        if (!byProfileForLookup.TryGetValue(spec.ParentProfile, out var parent))
        {
          _logger.LogWarning("Reconciler: analyzer {AnalyzerId} declared spec for unknown parent profile '{Parent}' on camera {CameraId}",
            producerId, spec.ParentProfile, cameraId);
          continue;
        }

        if (existingForProducer.TryGetValue(spec.Profile, out var existing))
        {
          var dirty = false;
          if (existing.DeletedAt != null) { existing.DeletedAt = null; dirty = true; }
          if (existing.FormatId != spec.FormatId) { existing.FormatId = spec.FormatId; dirty = true; }
          if (existing.Kind != spec.Kind) { existing.Kind = spec.Kind; dirty = true; }
          if (existing.ParentStreamId != parent.Id) { existing.ParentStreamId = parent.Id; dirty = true; }
          if (existing.Codec != spec.Codec) { existing.Codec = spec.Codec; dirty = true; }
          if (dirty)
            await _plugins.DataProvider.Streams.UpsertAsync(existing, ct);
        }
        else
        {
          var row = new CameraStream
          {
            Id = Guid.NewGuid(),
            CameraId = cameraId,
            Profile = spec.Profile,
            Kind = spec.Kind,
            FormatId = spec.FormatId,
            Codec = spec.Codec,
            ParentStreamId = parent.Id,
            ProducerId = producerId
          };
          await _plugins.DataProvider.Streams.UpsertAsync(row, ct);
        }
      }

      var declared = specs.Select(s => s.Profile).ToHashSet();
      foreach (var (profile, row) in existingForProducer)
      {
        if (declared.Contains(profile)) continue;
        if (row.DeletedAt != null) continue;
        row.DeletedAt = DateTimeOffset.UtcNow.ToUnixMicroseconds();
        await _plugins.DataProvider.Streams.UpsertAsync(row, ct);
      }
    }

    return new Success();
  }

  public async Task<OneOf<Success, Error>> ReconcileAllAsync(CancellationToken ct)
  {
    var camerasResult = await _plugins.DataProvider.Cameras.GetAllAsync(ct);
    if (camerasResult.IsT1) return camerasResult.AsT1;

    foreach (var camera in camerasResult.AsT0)
    {
      var result = await ReconcileCameraAsync(camera.Id, ct);
      if (result.IsT1)
        _logger.LogWarning("Reconciler: camera {CameraId} failed: {Message}",
          camera.Id, result.AsT1.Message);
    }

    return new Success();
  }
}
