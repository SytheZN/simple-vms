using Server.Core;
using Shared.Models;
using Shared.Models.Entities;

namespace Server.Streaming;

public sealed class CameraRegistry : ICameraRegistry
{
  private readonly IDataProvider _dataProvider;
  private readonly CameraStatusTracker _statusTracker;

  public CameraRegistry(IDataProvider dataProvider, CameraStatusTracker statusTracker)
  {
    _dataProvider = dataProvider;
    _statusTracker = statusTracker;
  }

  public async Task<OneOf<IReadOnlyList<CameraInfo>, Error>> GetCamerasAsync(CancellationToken ct)
  {
    var result = await _dataProvider.Cameras.GetAllAsync(ct);
    return await result.Match<Task<OneOf<IReadOnlyList<CameraInfo>, Error>>>(
      async cameras =>
      {
        var infos = new List<CameraInfo>(cameras.Count);
        foreach (var camera in cameras)
        {
          var projected = await ProjectAsync(camera, ct);
          if (projected.IsT1) return projected.AsT1;
          infos.Add(projected.AsT0);
        }
        return infos;
      },
      error => Task.FromResult<OneOf<IReadOnlyList<CameraInfo>, Error>>(error));
  }

  public async Task<OneOf<CameraInfo, Error>> GetCameraAsync(Guid cameraId, CancellationToken ct)
  {
    var result = await _dataProvider.Cameras.GetByIdAsync(cameraId, ct);
    return await result.Match<Task<OneOf<CameraInfo, Error>>>(
      camera => ProjectAsync(camera, ct),
      error => Task.FromResult<OneOf<CameraInfo, Error>>(error));
  }

  private async Task<OneOf<CameraInfo, Error>> ProjectAsync(Camera camera, CancellationToken ct)
  {
    var result = await _dataProvider.Streams.GetByCameraIdAsync(camera.Id, ct);
    return result.Match<OneOf<CameraInfo, Error>>(
      streams => new CameraInfo
      {
        Id = camera.Id,
        Name = camera.Name,
        Address = camera.Address,
        ProviderId = camera.ProviderId,
        Streams = streams.Where(s => s.Uri != null).Select(ToProfile).ToList(),
        Capabilities = camera.Capabilities
      },
      error => error);
  }

  private static StreamProfile ToProfile(CameraStream s) => new()
  {
    Id = s.Id,
    Profile = s.Profile,
    Kind = s.Kind,
    FormatId = s.FormatId,
    Codec = s.Codec,
    Resolution = s.Resolution,
    Fps = s.Fps,
    Bitrate = s.Bitrate,
    Uri = s.Uri!,
    IsRootStream = s.ParentStreamId == null && s.ProducerId == null
  };
}
