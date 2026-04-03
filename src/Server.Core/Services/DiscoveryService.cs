using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class DiscoveryService
{
  private readonly IPluginHost _plugins;

  public DiscoveryService(IPluginHost plugins)
  {
    _plugins = plugins;
  }

  public async Task<OneOf<IReadOnlyList<DiscoveredCameraDto>, Error>> DiscoverAsync(
    DiscoveryRequest request, CancellationToken ct)
  {
    var ports = request.Ports?
      .Where(p => p is >= 1 and <= 65535)
      .ToArray();

    var options = new DiscoveryOptions
    {
      Subnets = request.Subnets,
      Ports = ports is { Length: > 0 } ? ports : null,
      Username = request.Credentials?.Username,
      Password = request.Credentials?.Password
    };

    var existing = await _plugins.DataProvider.Cameras.GetAllAsync(ct);
    var existingAddresses = existing.Match(
      cameras => cameras.Select(c => c.Address).ToHashSet(StringComparer.OrdinalIgnoreCase),
      _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    var results = new List<DiscoveredCameraDto>();

    foreach (var provider in _plugins.CameraProviders)
    {
      try
      {
        var discovered = await provider.DiscoverAsync(options, ct);
        foreach (var cam in discovered)
        {
          results.Add(new DiscoveredCameraDto
          {
            Address = cam.Address,
            Hostname = cam.Hostname,
            Name = cam.Name,
            Manufacturer = cam.Manufacturer,
            Model = cam.Model,
            ProviderId = cam.ProviderId,
            AlreadyAdded = existingAddresses.Contains(cam.Address)
          });
        }
      }
      catch
      {
        // provider failed; continue with others
      }
    }

    return results;
  }
}
