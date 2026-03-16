using Server.Core.Services;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Api.Endpoints;

public static class RetentionEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/retention");
    group.MapGet("/", GetGlobal);
    group.MapPut("/", SetGlobal);
  }

  private static async Task<IResult> GetGlobal(
    RetentionService retention,
    CancellationToken ct)
  {
    var result = await retention.GetGlobalAsync(ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.Retention, 0x0010));
  }

  private static async Task<IResult> SetGlobal(
    RetentionPolicy policy,
    RetentionService retention,
    CancellationToken ct)
  {
    var result = await retention.SetGlobalAsync(policy, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.Retention, 0x0011));
  }
}
