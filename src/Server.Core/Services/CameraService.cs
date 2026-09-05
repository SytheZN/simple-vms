using System.Collections.Concurrent;
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
  private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _reprobeLocks = new();
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
    WatchCameraReprobeRequested(_eventCts.Token);
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _eventCts?.Cancel();
    _eventCts?.Dispose();
    _eventCts = null;
    return Task.CompletedTask;
  }

  private void WatchCameraReprobeRequested(CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in _eventBus.SubscribeAsync<CameraReprobeRequested>(ct))
        _ = HandleReprobeAsync(evt, ct);
    }, ct);
  }

  private async Task HandleReprobeAsync(CameraReprobeRequested evt, CancellationToken ct)
  {
    var sem = _reprobeLocks.GetOrAdd(evt.CameraId, _ => new SemaphoreSlim(1, 1));
    if (!await sem.WaitAsync(0, ct))
    {
      _logger.LogDebug(
        "Reprobe already active for camera {CameraId}; dropping request from {Initiator}",
        evt.CameraId, evt.Initiator);
      return;
    }

    try
    {
      var result = await RefreshAsync(evt.CameraId, ct);
      if (result.IsT1)
        _logger.LogError(
          "Reprobe failed for camera {CameraId} (initiator: {Initiator}): {Message}",
          evt.CameraId, evt.Initiator, result.AsT1.Message);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Reprobe threw for camera {CameraId} (initiator: {Initiator})",
        evt.CameraId, evt.Initiator);
    }
    finally
    {
      sem.Release();
    }
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
                .Select(x => ToStreamDto(cam.Id, x, id => byId.TryGetValue(id, out var v) ? v : null))
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
            return s.Select(x => ToStreamDto(cam.Id, x, id => byId.TryGetValue(id, out var v) ? v : null)).ToList();
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
    var originalName = camera.Name;
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

    var nameChanged = camera.Name != originalName;
    var addressChanged = camera.Address != originalAddress;
    var providerChanged = camera.ProviderId != originalProviderId;
    var rtspPort = camera.Config.GetValueOrDefault("rtspPortOverride");
    var rtspPortChanged = rtspPort != originalRtspPort;
    if (nameChanged || addressChanged || providerChanged
      || request.Credentials != null || rtspPortChanged)
    {
      await _eventBus.PublishAsync(new CameraUpdated
      {
        CameraId = id,
        Name = camera.Name,
        PreviousName = nameChanged ? originalName : null,
        Address = addressChanged ? camera.Address : null,
        ProviderId = providerChanged ? camera.ProviderId : null,
        CredentialsUpdated = request.Credentials != null,
        RtspPortOverride = rtspPortChanged ? rtspPort ?? "" : null,
        Timestamp = camera.UpdatedAt
      }, ct);
    }

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

        var diff = new Dictionary<string, DiffChange>();
        var streamDtos = new List<StreamProfileDto>();
        foreach (var s in config.Streams)
        {
          if (existingProfiles.TryGetValue(s.Profile, out var existing))
          {
            var uri = rtspPortOverride.HasValue ? RewriteRtspPort(s.Uri, rtspPortOverride.Value) : s.Uri;
            DiffStreamUpdate(diff, id, existing, s, uri);
            existing.Codec = s.Codec;
            existing.Resolution = s.Resolution;
            existing.Fps = s.Fps;
            existing.Bitrate = s.Bitrate;
            existing.Uri = uri;
            await _plugins.DataProvider.Streams.UpsertAsync(existing, ct);
            streamDtos.Add(ToStreamDto(id, existing));
          }
          else
          {
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
            diff[ConfigDiff.Stream(id, stream.Id)] = new DiffChange
            {
              Type = DiffChangeType.Add,
              NewValue = stream.Profile
            };
            await _plugins.DataProvider.Streams.UpsertAsync(stream, ct);
            streamDtos.Add(ToStreamDto(id, stream));
          }
        }

        var newProfiles = config.Streams.Select(s => s.Profile).ToHashSet();
        foreach (var (profile, existing) in existingProfiles)
        {
          if (!newProfiles.Contains(profile))
          {
            diff[ConfigDiff.Stream(id, existing.Id)] = new DiffChange
            {
              Type = DiffChangeType.Remove,
              OldValue = existing.Profile
            };
            await _plugins.DataProvider.Streams.DeleteAsync(existing.Id, ct);
          }
        }

        await SyncDerivedStreamsAsync(_plugins, id, diff, _logger, ct);

        if (diff.Count > 0)
        {
          await _eventBus.PublishAsync(new CameraConfigChanged
          {
            CameraId = id,
            Diff = diff,
            Timestamp = camera.UpdatedAt
          }, ct);
        }

        return ToCameraListItem(camera, _status.GetStatus(id), streamDtos);
      },
      err => Task.FromResult<OneOf<CameraDto, Error>>(err));
  }

  private static void DiffStreamUpdate(
    Dictionary<string, DiffChange> diff, Guid cameraId, CameraStream existing,
    SourceStreamSpec probed, string uri)
  {
    var sid = existing.Id;
    if (!string.Equals(existing.Codec, probed.Codec, StringComparison.Ordinal))
      diff[ConfigDiff.StreamField(cameraId, sid, ConfigDiff.FieldCodec)] = new DiffChange
      {
        Type = DiffChangeType.Update,
        OldValue = existing.Codec,
        NewValue = probed.Codec
      };
    if (!string.Equals(existing.Resolution, probed.Resolution, StringComparison.Ordinal))
      diff[ConfigDiff.StreamField(cameraId, sid, ConfigDiff.FieldResolution)] = new DiffChange
      {
        Type = DiffChangeType.Update,
        OldValue = existing.Resolution,
        NewValue = probed.Resolution
      };
    if (existing.Fps != probed.Fps)
      diff[ConfigDiff.StreamField(cameraId, sid, ConfigDiff.FieldFps)] = new DiffChange
      {
        Type = DiffChangeType.Update,
        OldValue = existing.Fps?.ToString(),
        NewValue = probed.Fps?.ToString()
      };
    if (existing.Bitrate != probed.Bitrate)
      diff[ConfigDiff.StreamField(cameraId, sid, ConfigDiff.FieldBitrate)] = new DiffChange
      {
        Type = DiffChangeType.Update,
        OldValue = existing.Bitrate?.ToString(),
        NewValue = probed.Bitrate?.ToString()
      };
    if (!string.Equals(existing.Uri, uri, StringComparison.Ordinal))
      diff[ConfigDiff.StreamField(cameraId, sid, ConfigDiff.FieldUri)] = new DiffChange
      {
        Type = DiffChangeType.Update,
        OldValue = existing.Uri,
        NewValue = uri
      };
  }

  public async Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct)
  {
    var cameraResult = await _plugins.DataProvider.Cameras.GetByIdAsync(id, ct);
    var cameraName = cameraResult.IsT0 ? cameraResult.AsT0.Name : "";

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
      Name = cameraName,
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

  private StreamProfileDto ToStreamDto(Guid cameraId, CameraStream s, Func<Guid, CameraStream?>? lookup = null)
  {
    bool recordingEnabled;
    if (s.Kind != StreamKind.Metadata)
      recordingEnabled = s.RecordingEnabled;
    else if (lookup == null)
      recordingEnabled = false;
    else
      recordingEnabled = IsDerivedStreamRecordable(cameraId, s)
        && StreamHierarchy.ResolveRootStream(s, lookup).RecordingEnabled;

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

  private bool IsDerivedStreamRecordable(Guid cameraId, CameraStream s)
  {
    if (s.ProducerId == null) return true;
    var analyzer = _plugins.Analyzers.FirstOrDefault(a => a.AnalyzerId == s.ProducerId);
    if (analyzer is not IDataStreamAnalyzerStreamOutput streamOutput) return true;
    return streamOutput.GetDerivedStreams(cameraId)
      .FirstOrDefault(spec => spec.Profile == s.Profile)?.Recordable ?? true;
  }

  internal static async Task SyncDerivedStreamsAsync(
    IPluginHost plugins,
    Guid cameraId,
    Dictionary<string, DiffChange> diff,
    ILogger logger,
    CancellationToken ct)
  {
    var streamsResult = await plugins.DataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1) return;
    var allStreams = streamsResult.AsT0;

    var sourcesByProfile = allStreams
      .Where(s => s.DeletedAt == null && s.ProducerId == null)
      .ToDictionary(s => s.Profile);

    foreach (var identity in plugins.Analyzers)
    {
      if (identity is not IDataStreamAnalyzerStreamOutput analyzer) continue;
      var producerId = identity.AnalyzerId;
      var specs = analyzer.GetDerivedStreams(cameraId);

      var existingForProducer = allStreams
        .Where(s => s.ProducerId == producerId)
        .ToDictionary(s => s.Profile);

      foreach (var spec in specs)
      {
        if (!sourcesByProfile.TryGetValue(spec.ParentProfile, out var parent))
        {
          logger.LogWarning(
            "Analyzer {AnalyzerId} declared spec for unknown parent profile '{Parent}' on camera {CameraId}",
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
            await plugins.DataProvider.Streams.UpsertAsync(existing, ct);
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
          await plugins.DataProvider.Streams.UpsertAsync(row, ct);
          diff[ConfigDiff.Stream(cameraId, row.Id)] = new DiffChange
          {
            Type = DiffChangeType.Add,
            NewValue = row.Profile
          };
        }
      }

      var declared = specs.Select(s => s.Profile).ToHashSet();
      foreach (var (profile, row) in existingForProducer)
      {
        if (declared.Contains(profile)) continue;
        if (row.DeletedAt != null) continue;
        row.DeletedAt = DateTimeOffset.UtcNow.ToUnixMicroseconds();
        await plugins.DataProvider.Streams.UpsertAsync(row, ct);
        diff[ConfigDiff.Stream(cameraId, row.Id)] = new DiffChange
        {
          Type = DiffChangeType.Remove,
          OldValue = row.Profile
        };
      }
    }
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
