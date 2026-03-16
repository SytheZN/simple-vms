using Server.Core.Services;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Api.Endpoints;

public static class DiscoveryEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.MapPost("/api/v1/discovery", Discover);
  }

  private static async Task<IResult> Discover(
    DiscoveryRequest request,
    DiscoveryService discovery,
    CancellationToken ct)
  {
    var result = await discovery.DiscoverAsync(request, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.Discovery, 0x0010));
  }
}
