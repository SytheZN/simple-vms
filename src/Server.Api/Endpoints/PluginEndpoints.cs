using Server.Core.Services;
using Shared.Models;

namespace Server.Api.Endpoints;

public static class PluginEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/plugins");
    group.MapGet("/", GetAll);
    group.MapGet("/{id}", GetById);
    group.MapPut("/{id}/config", UpdateConfig);
    group.MapPost("/{id}/start", Start);
    group.MapPost("/{id}/stop", Stop);
  }

  private static IResult GetAll(PluginService plugins)
  {
    var result = plugins.GetAll();
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0010));
  }

  private static IResult GetById(string id, PluginService plugins)
  {
    var result = plugins.GetById(id);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0011));
  }

  private static async Task<IResult> UpdateConfig(
    string id,
    Dictionary<string, object> config,
    PluginService plugins,
    CancellationToken ct)
  {
    var result = await plugins.UpdateConfigAsync(id, config, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0014));
  }

  private static async Task<IResult> Start(
    string id,
    PluginService plugins,
    CancellationToken ct)
  {
    var result = await plugins.StartAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0012));
  }

  private static async Task<IResult> Stop(
    string id,
    PluginService plugins)
  {
    var result = await plugins.StopAsync(id);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0013));
  }
}
