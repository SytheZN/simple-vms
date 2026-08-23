using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;
using Shared.Api;
using Shared.Models.Entities;
using Shared.Models.Events;

namespace Server.Core.Services;

public sealed class CameraService : IHostedService
{
  private readonly IPluginHost _plugins;
  private readonly CameraStatusTracker _status;
  private readonly IEventBus _eventBus;
  private readonly ILogger<CameraService> _logger;
  private CancellationTokenSource? _eventCts;

  public CameraService(IPluginHost plugins, CameraStatusTracker status, IEventBus eventBus, ILogger<CameraService> logger)
  {
    _plugins = plugins;
    _status = status;
    _eventBus = eventBus;
    _logger = logger;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _eventCts = new CancellationTokenSource();
    WatchPipelineConfigMismatch(_eventCts.Token);
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _eventCts?.Cancel();
    _eventCts?.Dispose();
    _eventCts = null;
    return Task.CompletedTask;
  }

  /// <summary>
  /// Stored stream metadata belongs to the camera provider, so a stream that no longer matches what
  /// was probed is re-read from the provider rather than inferred from the bitstream.
  /// </summary>
  private void WatchPipelineConfigMismatch(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<PipelineConfigMismatch>(ct))
      {
        try
        {
          var cameraId = await FindCameraByStreamUriAsync(evt.Uri, ct);
          if (cameraId == null)
          {
            _logger.LogWarning("No camera owns stream {Uri}; cannot refresh after it changed", evt.Uri);
            continue;
          }

          var refreshed = await RefreshAsync(cameraId.Value, ct);
          if (refreshed.IsT1)
            _logger.LogError("Refresh failed for camera {CameraId} after {Uri} changed: {Message}",
              cameraId, evt.Uri, refreshed.AsT1.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          _logger.LogError(ex, "Failed to refresh camera after {Uri} changed", evt.Uri);
        }
      }
    }, ct);
  }

  private async Task<Guid?> FindCameraByStreamUriAsync(string uri, CancellationToken ct)
  {
    var camerasResult = await _plugins.DataProvider.Cameras.GetAllAsync(ct);
    if (camerasResult.IsT1) return null;

    foreach (var camera in camerasResult.AsT0)
    {
      var streams = await _plugins.DataProvider.Streams.GetByCameraIdAsync(camera.Id, ct);
      if (streams.IsT1) continue;
      if (streams.AsT0.Any(s => s.Uri == uri))
        return camera.Id;
    }

    return null;
  }

  public async Task<OneOf<IReadOnlyList<CameraDto>, Error>> GetAllAsync(
    string? statusFilter, CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Cameras.GetAllAsync(ct);
    return await result.Match<Task<OneOf<IReadOnlyList<CameraDto>, Error>>>(
      async cameras =>
      {
        var items = new List<CameraDto>();
        foreach (var cam in cameras)
        {
          var cameraStatus = _status.GetStatus(cam.Id);
          if (statusFilter != null && cameraStatus != statusFilter)
            continue;

          var streams = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cam.Id, ct);
          var streamDtos = streams.Match(
            s =>
            {
              var byId = s.ToDictionary(x => x.Id);
              return s.Where(x => x.DeletedAt == null)
                .Select(x => ToStreamDto(x, id => byId.TryGetValue(id, out var v) ? v : null))
                .ToList();
            },
            _ => new List<StreamProfileDto>());

          items.Add(ToCameraListItem(cam, cameraStatus, streamDtos));
        }
        return (OneOf<IReadOnlyList<CameraDto>, Error>)items;
      },
      error => Task.FromResult<OneOf<IReadOnlyList<CameraDto>, Error>>(error));
  }

  public async Task<OneOf<CameraDto, Error>> GetByIdAsync(
    Guid id, CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Cameras.GetByIdAsync(id, ct);
    return await result.Match<Task<OneOf<CameraDto, Error>>>(
      async cam =>
      {
        var streams = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cam.Id, ct);
        var streamDtos = streams.Match(
          s =>
          {
            var byId = s.ToDictionary(x => x.Id);
            return s.Select(x => ToStreamDto(x, id => byId.TryGetValue(id, out var v) ? v : null)).ToList();
          },
          _ => new List<StreamProfileDto>());
        return ToCameraListItem(cam, _status.GetStatus(cam.Id), streamDtos);
      },
      error => Task.FromResult<OneOf<CameraDto, Error>>(error));
  }

  public async Task<OneOf<ProbeResponse, Error>> ProbeAsync(
    ProbeRequest request, CancellationToken ct)
  {
    var address = NormalizeOnvifAddress(request.Address);
    var provider = request.ProviderId != null
      ? _plugins.CameraProviders.FirstOrDefault(p => p.ProviderId == request.ProviderId)
      : _plugins.CameraProviders.FirstOrDefault();

    if (provider == null)
      return new Error(
        Result.BadRequest,
        new DebugTag(ModuleIds.CameraManagement, 0x0001),
        "No camera provider available");

    var creds = request.Credentials != null
      ? Credentials.FromUserPass(request.Credentials.Username, request.Credentials.Password)
      : Credentials.FromUserPass("", "");

    CameraConfiguration config;
    try
    {
      config = await provider.ConfigureAsync(address, creds, ct);
    }
    catch (Exception ex)
    {
      return new Error(
        Result.InternalError,
        new DebugTag(ModuleIds.CameraManagement, 0x0003),
        $"Failed to configure camera: {ex.Message}");
    }

    return new ProbeResponse
    {
      Name = config.Name,
      Streams = config.Streams.Select(s => new StreamProfileDto
      {
        Profile = s.Profile,
        Kind = s.Kind,
        FormatId = s.FormatId,
        Codec = s.Codec ?? "",
        Resolution = s.Resolution ?? "",
        Fps = s.Fps ?? 0,
        RecordingEnabled = false
      }).ToList(),
      Capabilities = config.Capabilities,
      Config = config.Config
    };
  }

  public async Task<OneOf<CameraDto, Error>> CreateAsync(
    CreateCameraRequest request, CancellationToken ct)
  {
    var address = NormalizeOnvifAddress(request.Address);

    var existingResult = await _plugins.DataProvider.Cameras.GetByAddressAsync(address, ct);
    if (existingResult.IsT0)
      return new Error(
        Result.Conflict,
        new DebugTag(ModuleIds.CameraManagement, 0x0002),
        $"Camera at address {address} already exists");

    var creds = request.Credentials != null
      ? Credentials.FromUserPass(request.Credentials.Username, request.Credentials.Password)
      : Credentials.FromUserPass("", "");

    var now = DateTimeOffset.UtcNow.ToUnixMicroseconds();
    var camera = new Camera
    {
      Id = Guid.NewGuid(),
      Name = request.Name ?? address,
      Address = address,
      ProviderId = request.ProviderId
        ?? _plugins.CameraProviders.FirstOrDefault()?.ProviderId
        ?? "onvif",
      Credentials = creds.Values.ToCredentialsJson(),
      CreatedAt = now,
      UpdatedAt = now
    };

    if (request.RtspPortOverride is > 0)
      camera.Config["rtspPortOverride"] = request.RtspPortOverride.Value.ToString();

    var createResult = await _plugins.DataProvider.Cameras.CreateAsync(camera, ct);
    if (createResult.IsT1)
      return createResult.AsT1;

    await _eventBus.PublishAsync(new CameraAdded
    {
      CameraId = camera.Id,
      Timestamp = now
    }, ct);

    return await RefreshAsync(camera.Id, ct);
  }

  public async Task<OneOf<CameraDto, Error>> UpdateAsync(
    Guid id, UpdateCameraRequest request, CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Cameras.GetByIdAsync(id, ct);
    if (result.IsT1) return result.AsT1;

    var camera = result.AsT0;
    var originalAddress = camera.Address;
    var originalProviderId = camera.ProviderId;
    var originalRtspPort = camera.Config.GetValueOrDefault("rtspPortOverride");

    camera.Name = request.Name ?? camera.Name;
    if (request.Address != null)
      camera.Address = NormalizeOnvifAddress(request.Address);
    if (request.ProviderId != null)
      camera.ProviderId = request.ProviderId;

    if (request.Credentials != null)
    {
      var creds = Credentials.FromUserPass(request.Credentials.Username, request.Credentials.Password);
      camera.Credentials = creds.Values.ToCredentialsJson();
    }

    if (request.RtspPortOverride is int port)
    {
      if (port > 0)
        camera.Config["rtspPortOverride"] = port.ToString();
      else
        camera.Config.Remove("rtspPortOverride");
    }
    camera.UpdatedAt = DateTimeOffset.UtcNow.ToUnixMicroseconds();

    var updateResult = await _plugins.DataProvider.Cameras.UpdateAsync(camera, ct);
    if (updateResult.IsT1) return updateResult.AsT1;

    var needsRefresh = request.Credentials != null
      || (request.Address != null && camera.Address != originalAddress)
      || (request.ProviderId != null && camera.ProviderId != originalProviderId)
      || (request.RtspPortOverride.HasValue
        && camera.Config.GetValueOrDefault("rtspPortOverride") != originalRtspPort);
    if (needsRefresh)
      return await RefreshAsync(id, ct);

    return await GetByIdAsync(id, ct);
  }

  public async Task<OneOf<CameraDto, Error>> RefreshAsync(Guid id, CancellationToken ct)
  {
    var result = await _plugins.DataProvider.Cameras.GetByIdAsync(id, ct);
    if (result.IsT1) return result.AsT1;

    var camera = result.AsT0;
    var provider = _plugins.CameraProviders.FirstOrDefault(p => p.ProviderId == camera.ProviderId)
      ?? _plugins.CameraProviders.FirstOrDefault();
    if (provider == null)
      return new Error(Result.BadRequest, new DebugTag(ModuleIds.CameraManagement, 0x0004),
        "No camera provider available");

    Credentials creds;
    if (camera.Credentials is { Length: > 0 })
    {
      var dict = camera.Credentials.ParseCredentials();
      creds = dict != null
        ? Credentials.FromUserPass(
            dict.TryGetValue("username", out var u) ? u : "",
            dict.TryGetValue("password", out var p) ? p : "")
        : Credentials.FromUserPass("", "");
    }
    else
    {
      creds = Credentials.FromUserPass("", "");
    }

    CameraConfiguration config;
    try
    {
      config = await provider.ConfigureAsync(camera.Address, creds, ct);
    }
    catch (Exception ex)
    {
      return new Error(Result.InternalError, new DebugTag(ModuleIds.CameraManagement, 0x0005),
        $"Failed to configure camera: {ex.Message}");
    }

    camera.Capabilities = config.Capabilities;
    var existingOverride = camera.Config.GetValueOrDefault("rtspPortOverride");
    camera.Config = new Dictionary<string, string>(config.Config);
    if (existingOverride != null)
      camera.Config["rtspPortOverride"] = existingOverride;
    camera.UpdatedAt = DateTimeOffset.UtcNow.ToUnixMicroseconds();
    await _plugins.DataProvider.Cameras.UpdateAsync(camera, ct);

    var existingStreamsResult = await _plugins.DataProvider.Streams.GetByCameraIdAsync(id, ct);
    return await existingStreamsResult.Match<Task<OneOf<CameraDto, Error>>>(
      async allExisting =>
      {
        var existingProfiles = allExisting
          .Where(s => s.ProducerId == null)
          .ToDictionary(s => s.Profile);

        var rtspPortOverride = camera.Config.TryGetValue("rtspPortOverride", out var portStr)
          && int.TryParse(portStr, out var port) ? (int?)port : null;

        var streamDtos = new List<StreamProfileDto>();
        var streamsChanged = false;
        foreach (var s in config.Streams)
        {
          if (existingProfiles.TryGetValue(s.Profile, out var existing))
          {
            existing.Codec = s.Codec;
            existing.Resolution = s.Resolution;
            existing.Fps = s.Fps;
            existing.Bitrate = s.Bitrate;
            var uri = rtspPortOverride.HasValue ? RewriteRtspPort(s.Uri, rtspPortOverride.Value) : s.Uri;
            if (existing.Uri != uri)
              streamsChanged = true;
            existing.Uri = uri;
            await _plugins.DataProvider.Streams.UpsertAsync(existing, ct);
            streamDtos.Add(ToStreamDto(existing));
          }
          else
          {
            streamsChanged = true;
            var uri = rtspPortOverride.HasValue ? RewriteRtspPort(s.Uri, rtspPortOverride.Value) : s.Uri;
            var stream = new CameraStream
            {
              Id = Guid.NewGuid(),
              CameraId = id,
              Profile = s.Profile,
              Kind = s.Kind,
              FormatId = s.FormatId,
              Codec = s.Codec,
              Resolution = s.Resolution,
              Fps = s.Fps,
              Bitrate = s.Bitrate,
              Uri = uri,
              RecordingEnabled = true
            };
            await _plugins.DataProvider.Streams.UpsertAsync(stream, ct);
            streamDtos.Add(ToStreamDto(stream));
          }
        }

        var newProfiles = config.Streams.Select(s => s.Profile).ToHashSet();
        foreach (var (profile, existing) in existingProfiles)
        {
          if (!newProfiles.Contains(profile))
          {
            streamsChanged = true;
            await _plugins.DataProvider.Streams.DeleteAsync(existing.Id, ct);
          }
        }

        if (streamsChanged)
        {
          await _eventBus.PublishAsync(new CameraConfigChanged
          {
            CameraId = id,
            Timestamp = camera.UpdatedAt
          }, ct);
        }

        return ToCameraListItem(camera, _status.GetStatus(id), streamDtos);
      },
      err => Task.FromResult<OneOf<CameraDto, Error>>(err));
  }

  public async Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct)
  {
    var streamsResult = await _plugins.DataProvider.Streams.GetByCameraIdAsync(id, ct);
    var streamIds = streamsResult.IsT0
      ? streamsResult.AsT0.Select(s => s.Id).ToList()
      : new List<Guid>();

    var result = await _plugins.DataProvider.Cameras.DeleteAsync(id, ct);
    if (result.IsT1) return result;

    foreach (var entry in _plugins.Plugins)
    {
      if (entry.Plugin is IPluginCameraSettings cameraSettings)
      {
        var cleanup = await cameraSettings.OnRemovedAsync(id, ct);
        if (cleanup.IsT1)
          _logger.LogWarning("Plugin {Plugin} OnRemovedAsync failed for camera {Camera}: {Error}",
            entry.Metadata.Id, id, cleanup.AsT1.Message);
      }
      if (entry.Plugin is IPluginStreamSettings streamSettings)
      {
        foreach (var streamId in streamIds)
        {
          var cleanup = await streamSettings.OnRemovedAsync(streamId, ct);
          if (cleanup.IsT1)
            _logger.LogWarning("Plugin {Plugin} OnRemovedAsync failed for stream {Stream}: {Error}",
              entry.Metadata.Id, streamId, cleanup.AsT1.Message);
        }
      }
    }

    _status.Remove(id);
    await _eventBus.PublishAsync(new CameraRemoved
    {
      CameraId = id,
      Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
    }, CancellationToken.None);

    return result;
  }

  public Task<OneOf<Success, Error>> RestartAsync(Guid id, CancellationToken ct)
  {
    return Task.FromResult<OneOf<Success, Error>>(new Error(
      Result.Unavailable,
      new DebugTag(ModuleIds.CameraManagement, 0x0006),
      "Streaming pipeline not available"));
  }

  public Task<OneOf<byte[], Error>> GetSnapshotAsync(Guid id, CancellationToken ct)
  {
    return Task.FromResult<OneOf<byte[], Error>>(new Error(
      Result.Unavailable,
      new DebugTag(ModuleIds.CameraManagement, 0x0007),
      "Snapshot not available"));
  }

  private static CameraDto ToCameraListItem(
    Camera cam, string status, List<StreamProfileDto> streams) =>
    new()
    {
      Id = cam.Id,
      Name = cam.Name,
      Address = cam.Address,
      Status = status,
      ProviderId = cam.ProviderId,
      Streams = streams,
      Capabilities = cam.Capabilities,
      Config = cam.Config,
      SegmentDuration = cam.SegmentDuration,
      RetentionMode = cam.RetentionMode == Shared.Models.RetentionMode.Default
        ? null : cam.RetentionMode.ToString().ToLowerInvariant(),
      RetentionValue = cam.RetentionMode == Shared.Models.RetentionMode.Default
        ? null : cam.RetentionValue
    };

  private static StreamProfileDto ToStreamDto(CameraStream s, Func<Guid, CameraStream?>? lookup = null)
  {
    var recordingEnabled = s.Kind == StreamKind.Metadata && lookup != null
      ? StreamHierarchy.ResolveRootStream(s, lookup).RecordingEnabled
      : s.RecordingEnabled;

    return new StreamProfileDto
    {
      Profile = s.Profile,
      Kind = s.Kind,
      FormatId = s.FormatId,
      Codec = s.Codec ?? "",
      Resolution = s.Resolution ?? "",
      Fps = s.Fps ?? 0m,
      RecordingEnabled = recordingEnabled
    };
  }

  internal static string NormalizeOnvifAddress(string address)
  {
    var trimmed = address.Trim();

    var withoutScheme = trimmed;
    if (trimmed.Contains("://"))
      withoutScheme = trimmed[(trimmed.IndexOf("://") + 3)..];
    else
      trimmed = "http://" + trimmed;

    if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
      return trimmed;

    var hasPath = withoutScheme.Contains('/');
    if (!hasPath)
      return new UriBuilder(uri) { Path = "/onvif/device_service" }.Uri.AbsoluteUri;

    return uri.AbsoluteUri;
  }

  internal static string RewriteRtspPort(string uri, int port)
  {
    if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
      return uri;
    var builder = new UriBuilder(parsed) { Port = port };
    return builder.Uri.ToString();
  }
}
