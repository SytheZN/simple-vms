using System.Collections.Concurrent;
using Shared.Models;

namespace Server.Streaming;

public sealed class StreamTapRegistry : IStreamTap
{
  private readonly ConcurrentDictionary<(Guid CameraId, string Profile), CameraPipeline> _pipelines = new();

  public void RegisterPipeline(CameraPipeline pipeline) =>
    _pipelines[(pipeline.CameraId, pipeline.Profile)] = pipeline;

  public void UnregisterPipeline(Guid cameraId, string profile) =>
    _pipelines.TryRemove((cameraId, profile), out _);

  public CameraPipeline? GetPipeline(Guid cameraId, string profile) =>
    _pipelines.GetValueOrDefault((cameraId, profile));

  public IReadOnlyCollection<CameraPipeline> Pipelines => _pipelines.Values.ToList();

  public async Task<OneOf<IDataStream, Error>> TapAsync(
    Guid cameraId, string profile, CancellationToken ct)
  {
    if (!_pipelines.TryGetValue((cameraId, profile), out var pipeline))
      return Error.Create(ModuleIds.Streaming, 0x0001, Result.NotFound,
        $"No pipeline for camera {cameraId} profile '{profile}'");

    return await pipeline.SubscribeDataAsync(ct);
  }

  public async Task<OneOf<IVideoStream, Error>> SubscribeVideoAsync(
    Guid cameraId, string profile, CancellationToken ct)
  {
    if (!_pipelines.TryGetValue((cameraId, profile), out var pipeline))
      return Error.Create(ModuleIds.Streaming, 0x0004, Result.NotFound,
        $"No pipeline for camera {cameraId} profile '{profile}'");

    return await pipeline.SubscribeVideoAsync(ct);
  }
}
