using System.Reflection;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class SystemService
{
  private readonly ulong _startTime = DateTimeOffset.UtcNow.ToUnixMicroseconds();
  private readonly IDataProvider _data;
  private readonly ICertificateService _certs;
  private readonly CameraStatusTracker _cameraStatus;
  private readonly IEnumerable<IStorageProvider> _storage;

  public SystemService(
    IDataProvider data,
    ICertificateService certs,
    CameraStatusTracker cameraStatus,
    IEnumerable<IStorageProvider> storage)
  {
    _data = data;
    _certs = certs;
    _cameraStatus = cameraStatus;
    _storage = storage;
  }

  public async Task<OneOf<HealthResponse, Error>> GetHealthAsync(CancellationToken ct)
  {
    var uptimeMicros = DateTimeOffset.UtcNow.ToUnixMicroseconds() - _startTime;
    var uptimeSeconds = (int)(uptimeMicros / 1_000_000);
    var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

    if (!_certs.HasCerts)
    {
      return new HealthResponse
      {
        Status = "missing-certs",
        Uptime = uptimeSeconds,
        Cameras = new CameraHealthCounts { Total = 0, Online = 0, Offline = 0, Error = 0 },
        Storage = new StorageResponse { Stores = [] },
        Version = version
      };
    }

    var camerasResult = await _data.Cameras.GetAllAsync(ct);
    if (camerasResult.IsT1) return camerasResult.AsT1;

    var cameras = camerasResult.AsT0;
    var counts = new CameraHealthCounts
    {
      Total = cameras.Count,
      Online = cameras.Count(c => _cameraStatus.GetStatus(c.Id) == "online"),
      Offline = cameras.Count(c => _cameraStatus.GetStatus(c.Id) == "offline"),
      Error = cameras.Count(c => _cameraStatus.GetStatus(c.Id) == "error")
    };

    var storageResult = await GetStorageAsync(ct);
    var storage = storageResult.Match(
      s => s,
      _ => new StorageResponse { Stores = [] });

    var status = counts.Error > 0 ? "degraded"
      : counts.Offline > 0 && counts.Total > 0 ? "degraded"
      : "healthy";

    return new HealthResponse
    {
      Status = status,
      Uptime = uptimeSeconds,
      Cameras = counts,
      Storage = storage,
      Version = version
    };
  }

  public async Task<OneOf<StorageResponse, Error>> GetStorageAsync(CancellationToken ct)
  {
    var stores = new List<StorageStoreDto>();
    foreach (var provider in _storage)
    {
      try
      {
        var stats = await provider.GetStatsAsync(ct);
        stores.Add(new StorageStoreDto
        {
          TotalBytes = stats.TotalBytes,
          UsedBytes = stats.UsedBytes,
          FreeBytes = stats.FreeBytes,
          RecordingBytes = stats.RecordingBytes
        });
      }
      catch
      {
        stores.Add(new StorageStoreDto
        {
          TotalBytes = -1,
          UsedBytes = -1,
          FreeBytes = -1,
          RecordingBytes = -1
        });
      }
    }

    return new StorageResponse { Stores = stores };
  }

  public async Task<OneOf<ServerSettings, Error>> GetSettingsAsync(CancellationToken ct)
  {
    var all = await _data.Settings.GetAllAsync(ct);
    if (all.IsT1) return all.AsT1;

    var settings = all.AsT0;
    return new ServerSettings
    {
      ServerName = settings.GetValueOrDefault("server.name"),
      ExternalEndpoint = settings.GetValueOrDefault("server.externalEndpoint"),
      SegmentDuration = int.TryParse(settings.GetValueOrDefault("server.segmentDuration"), out var sd)
        ? sd : null,
      DiscoverySubnets = settings.GetValueOrDefault("server.discoverySubnets")
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
      DefaultCredentials = ReadCredentials(settings)
    };
  }

  public async Task<OneOf<Success, Error>> UpdateSettingsAsync(
    ServerSettings request, CancellationToken ct)
  {
    if (request.ServerName != null)
    {
      var r = await _data.Settings.SetAsync("server.name", request.ServerName, ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.ExternalEndpoint != null)
    {
      var r = await _data.Settings.SetAsync("server.externalEndpoint", request.ExternalEndpoint, ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.SegmentDuration.HasValue)
    {
      var r = await _data.Settings.SetAsync("server.segmentDuration",
        request.SegmentDuration.Value.ToString(), ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.DiscoverySubnets != null)
    {
      var r = await _data.Settings.SetAsync("server.discoverySubnets",
        string.Join(',', request.DiscoverySubnets), ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.DefaultCredentials != null)
    {
      var r = await _data.Settings.SetAsync("server.defaultCredentials.username",
        request.DefaultCredentials.Username, ct);
      if (r.IsT1) return r.AsT1;
      r = await _data.Settings.SetAsync("server.defaultCredentials.password",
        request.DefaultCredentials.Password, ct);
      if (r.IsT1) return r.AsT1;
    }

    return new Success();
  }

  private static CredentialsDto? ReadCredentials(IReadOnlyDictionary<string, string> settings)
  {
    var username = settings.GetValueOrDefault("server.defaultCredentials.username");
    var password = settings.GetValueOrDefault("server.defaultCredentials.password");

    if (username == null && password == null)
      return null;

    return new CredentialsDto
    {
      Username = username ?? "",
      Password = password ?? ""
    };
  }
}
