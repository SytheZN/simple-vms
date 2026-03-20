using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class SystemService
{
  private readonly IPluginHost _plugins;
  private readonly SystemHealth _health;

  public SystemService(IPluginHost plugins, SystemHealth health)
  {
    _plugins = plugins;
    _health = health;
  }

  public HealthResponse GetHealth()
  {
    return new HealthResponse
    {
      Status = _health.Status,
      Uptime = _health.Uptime,
      Version = _health.Version
    };
  }

  public async Task<OneOf<StorageResponse, Error>> GetStorageAsync(CancellationToken ct)
  {
    var stores = new List<StorageStoreDto>();
    foreach (var provider in _plugins.StorageProviders)
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
    var all = await _plugins.DataProvider.Config.GetAllAsync("server", ct);
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
      var r = await _plugins.DataProvider.Config.SetAsync("server", "server.name", request.ServerName, ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.ExternalEndpoint != null)
    {
      var r = await _plugins.DataProvider.Config.SetAsync("server", "server.externalEndpoint", request.ExternalEndpoint, ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.SegmentDuration.HasValue)
    {
      var r = await _plugins.DataProvider.Config.SetAsync("server", "server.segmentDuration",
        request.SegmentDuration.Value.ToString(), ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.DiscoverySubnets != null)
    {
      var r = await _plugins.DataProvider.Config.SetAsync("server", "server.discoverySubnets",
        string.Join(',', request.DiscoverySubnets), ct);
      if (r.IsT1) return r.AsT1;
    }
    if (request.DefaultCredentials != null)
    {
      var r = await _plugins.DataProvider.Config.SetAsync("server", "server.defaultCredentials.username",
        request.DefaultCredentials.Username, ct);
      if (r.IsT1) return r.AsT1;
      r = await _plugins.DataProvider.Config.SetAsync("server", "server.defaultCredentials.password",
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
