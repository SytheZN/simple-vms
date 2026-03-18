using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class DiscoveryService
{
  private readonly PluginHost _plugins;

  public DiscoveryService(PluginHost plugins)
  {
    _plugins = plugins;
  }

  public async Task<OneOf<IReadOnlyList<DiscoveredCameraDto>, Error>> DiscoverAsync(
    DiscoveryRequest request, CancellationToken ct)
  {
    var options = new DiscoveryOptions
    {
      Subnets = request.Subnets,
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
