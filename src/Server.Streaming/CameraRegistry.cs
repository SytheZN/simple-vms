using Server.Core;
using Shared.Models;

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

  public async Task<IReadOnlyList<CameraInfo>> GetCamerasAsync(CancellationToken ct)
  {
    var result = await _dataProvider.Cameras.GetAllAsync(ct);
    if (result.IsT1)
      return [];

    var cameras = result.AsT0;
    var infos = new List<CameraInfo>(cameras.Count);

    foreach (var camera in cameras)
    {
      var streams = await _dataProvider.Streams.GetByCameraIdAsync(camera.Id, ct);
      var profiles = streams.IsT0
        ? streams.AsT0.Where(s => s.Uri != null).Select(s => new StreamProfile
          {
            Profile = s.Profile,
            Kind = s.Kind,
            FormatId = s.FormatId,
            Codec = s.Codec,
            Resolution = s.Resolution,
            Fps = s.Fps,
            Bitrate = s.Bitrate,
            Uri = s.Uri!
          }).ToList()
        : [];

      infos.Add(new CameraInfo
      {
        Id = camera.Id,
        Name = camera.Name,
        Address = camera.Address,
        ProviderId = camera.ProviderId,
        Streams = profiles,
        Capabilities = camera.Capabilities
      });
    }

    return infos;
  }

  public async Task<CameraInfo?> GetCameraAsync(Guid cameraId, CancellationToken ct)
  {
    var result = await _dataProvider.Cameras.GetByIdAsync(cameraId, ct);
    if (result.IsT1)
      return null;

    var camera = result.AsT0;
    var streams = await _dataProvider.Streams.GetByCameraIdAsync(camera.Id, ct);
    var profiles = streams.IsT0
      ? streams.AsT0.Where(s => s.Uri != null).Select(s => new StreamProfile
        {
          Profile = s.Profile,
          Kind = s.Kind,
          FormatId = s.FormatId,
          Codec = s.Codec,
          Resolution = s.Resolution,
          Fps = s.Fps,
          Bitrate = s.Bitrate,
          Uri = s.Uri!
        }).ToList()
      : [];

    return new CameraInfo
    {
      Id = camera.Id,
      Name = camera.Name,
      Address = camera.Address,
      ProviderId = camera.ProviderId,
      Streams = profiles,
      Capabilities = camera.Capabilities
    };
  }
}
