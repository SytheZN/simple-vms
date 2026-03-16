using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class CameraService
{
  private readonly IDataProvider _data;
  private readonly CameraStatusTracker _status;
  private readonly IEnumerable<ICameraProvider> _providers;

  public CameraService(
    IDataProvider data,
    CameraStatusTracker status,
    IEnumerable<ICameraProvider> providers)
  {
    _data = data;
    _status = status;
    _providers = providers;
  }

  public async Task<OneOf<IReadOnlyList<CameraListItem>, Error>> GetAllAsync(
    string? statusFilter, CancellationToken ct)
  {
    var result = await _data.Cameras.GetAllAsync(ct);
    return await result.Match<Task<OneOf<IReadOnlyList<CameraListItem>, Error>>>(
      async cameras =>
      {
        var items = new List<CameraListItem>();
        foreach (var cam in cameras)
        {
          var cameraStatus = _status.GetStatus(cam.Id);
          if (statusFilter != null && cameraStatus != statusFilter)
            continue;

          var streams = await _data.Streams.GetByCameraIdAsync(cam.Id, ct);
          var streamDtos = streams.Match(
            s => s.Select(ToStreamDto).ToList(),
            _ => new List<StreamProfileDto>());

          items.Add(ToCameraListItem(cam, cameraStatus, streamDtos));
        }
        return (OneOf<IReadOnlyList<CameraListItem>, Error>)items;
      },
      error => Task.FromResult<OneOf<IReadOnlyList<CameraListItem>, Error>>(error));
  }

  public async Task<OneOf<CameraListItem, Error>> GetByIdAsync(
    Guid id, CancellationToken ct)
  {
    var result = await _data.Cameras.GetByIdAsync(id, ct);
    return await result.Match<Task<OneOf<CameraListItem, Error>>>(
      async cam =>
      {
        var streams = await _data.Streams.GetByCameraIdAsync(cam.Id, ct);
        var streamDtos = streams.Match(
          s => s.Select(ToStreamDto).ToList(),
          _ => new List<StreamProfileDto>());
        return ToCameraListItem(cam, _status.GetStatus(cam.Id), streamDtos);
      },
      error => Task.FromResult<OneOf<CameraListItem, Error>>(error));
  }

  public async Task<OneOf<CameraListItem, Error>> CreateAsync(
    CreateCameraRequest request, CancellationToken ct)
  {
    var provider = request.ProviderId != null
      ? _providers.FirstOrDefault(p => p.ProviderId == request.ProviderId)
      : _providers.FirstOrDefault();

    if (provider == null)
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.CameraManagement, 0x0001),
        "No camera provider available");

    var existingResult = await _data.Cameras.GetByAddressAsync(request.Address, ct);
    if (existingResult.IsT0)
      return new Error(
        Result.Conflict,
        new DebugTag(ModuleIds.CameraManagement, 0x0002),
        $"Camera at address {request.Address} already exists");

    var creds = request.Credentials != null
      ? new Credentials { Username = request.Credentials.Username, Password = request.Credentials.Password }
      : new Credentials { Username = "", Password = "" };

    CameraConfiguration config;
    try
    {
      config = await provider.ConfigureAsync(request.Address, creds, ct);
    }
    catch (Exception ex)
    {
      return new Error(
        Result.InternalError,
        new DebugTag(ModuleIds.CameraManagement, 0x0003),
        $"Failed to configure camera: {ex.Message}");
    }

    var now = DateTimeOffset.UtcNow.ToUnixMicroseconds();
    var camera = new Camera
    {
      Id = Guid.NewGuid(),
      Name = request.Name ?? config.Name,
      Address = request.Address,
      ProviderId = provider.ProviderId,
      Capabilities = config.Capabilities,
      CreatedAt = now,
      UpdatedAt = now
    };

    var createResult = await _data.Cameras.CreateAsync(camera, ct);
    if (createResult.IsT1)
      return createResult.AsT1;

    var streamDtos = new List<StreamProfileDto>();
    foreach (var s in config.Streams)
      {
        var stream = new CameraStream
        {
          Id = Guid.NewGuid(),
          CameraId = camera.Id,
          Profile = s.Profile,
          FormatId = s.FormatId,
          Codec = s.Codec,
          Resolution = s.Resolution,
          Fps = s.Fps,
          Bitrate = s.Bitrate,
          Uri = s.Uri,
          RecordingEnabled = true
        };
        await _data.Streams.UpsertAsync(stream, ct);
        streamDtos.Add(ToStreamDto(stream));
      }

    return ToCameraListItem(camera, "offline", streamDtos);
  }

  public async Task<OneOf<Success, Error>> UpdateAsync(
    Guid id, UpdateCameraRequest request, CancellationToken ct)
  {
    var result = await _data.Cameras.GetByIdAsync(id, ct);
    if (result.IsT1) return result.AsT1;

    var camera = result.AsT0;

    if (request.Name != null) camera.Name = request.Name;
    if (request.SegmentDuration.HasValue) camera.SegmentDuration = request.SegmentDuration;
    if (request.Retention != null)
    {
      camera.RetentionMode = Enum.Parse<RetentionMode>(request.Retention.Mode, ignoreCase: true);
      camera.RetentionValue = request.Retention.Value;
    }
    camera.UpdatedAt = DateTimeOffset.UtcNow.ToUnixMicroseconds();

    var updateResult = await _data.Cameras.UpdateAsync(camera, ct);
    if (updateResult.IsT1) return updateResult.AsT1;

    if (request.Streams != null)
    {
      var streams = await _data.Streams.GetByCameraIdAsync(id, ct);
      if (streams.IsT0)
      {
        foreach (var streamCfg in request.Streams)
        {
          var match = streams.AsT0.FirstOrDefault(s => s.Profile == streamCfg.Profile);
          if (match != null)
          {
            match.RecordingEnabled = streamCfg.RecordingEnabled;
            await _data.Streams.UpsertAsync(match, ct);
          }
        }
      }
    }

    return new Success();
  }

  public async Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct)
  {
    _status.Remove(id);
    return await _data.Cameras.DeleteAsync(id, ct);
  }

  public Task<OneOf<Success, Error>> RestartAsync(Guid id, CancellationToken ct)
  {
    return Task.FromResult<OneOf<Success, Error>>(new Error(
      Result.Unavailable,
      new DebugTag(ModuleIds.CameraManagement, 0x0010),
      "Streaming pipeline not available"));
  }

  public Task<OneOf<byte[], Error>> GetSnapshotAsync(Guid id, CancellationToken ct)
  {
    return Task.FromResult<OneOf<byte[], Error>>(new Error(
      Result.Unavailable,
      new DebugTag(ModuleIds.CameraManagement, 0x0011),
      "Snapshot not available"));
  }

  private static CameraListItem ToCameraListItem(
    Camera cam, string status, List<StreamProfileDto> streams) =>
    new()
    {
      Id = cam.Id,
      Name = cam.Name,
      Address = cam.Address,
      Status = status,
      ProviderId = cam.ProviderId,
      Streams = streams,
      Capabilities = cam.Capabilities
    };

  private static StreamProfileDto ToStreamDto(CameraStream s) =>
    new()
    {
      Profile = s.Profile,
      Codec = s.Codec ?? "",
      Resolution = s.Resolution ?? "",
      Fps = s.Fps ?? 0,
      RecordingEnabled = s.RecordingEnabled
    };
}
