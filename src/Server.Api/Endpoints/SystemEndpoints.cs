using Server.Core.Services;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Api.Endpoints;

public static class SystemEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/system");
    group.MapGet("/health", GetHealth);
    group.MapGet("/storage", GetStorage);
    group.MapGet("/settings", GetSettings);
    group.MapPut("/settings", UpdateSettings);
  }

  private static async Task<IResult> GetHealth(
    SystemService system,
    CancellationToken ct)
  {
    var result = await system.GetHealthAsync(ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.SystemManagement, 0x0010));
  }

  private static async Task<IResult> GetStorage(
    SystemService system,
    CancellationToken ct)
  {
    var result = await system.GetStorageAsync(ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.SystemManagement, 0x0011));
  }

  private static async Task<IResult> GetSettings(
    SystemService system,
    CancellationToken ct)
  {
    var result = await system.GetSettingsAsync(ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.SystemManagement, 0x0012));
  }

  private static async Task<IResult> UpdateSettings(
    ServerSettings settings,
    SystemService system,
    CancellationToken ct)
  {
    var result = await system.UpdateSettingsAsync(settings, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.SystemManagement, 0x0013));
  }
}
