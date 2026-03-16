using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class DiscoveryService
{
  private readonly IDataProvider _data;
  private readonly IEnumerable<ICameraProvider> _providers;

  public DiscoveryService(IDataProvider data, IEnumerable<ICameraProvider> providers)
  {
    _data = data;
    _providers = providers;
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

    var existing = await _data.Cameras.GetAllAsync(ct);
    var existingAddresses = existing.Match(
      cameras => cameras.Select(c => c.Address).ToHashSet(StringComparer.OrdinalIgnoreCase),
      _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    var results = new List<DiscoveredCameraDto>();

    foreach (var provider in _providers)
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
