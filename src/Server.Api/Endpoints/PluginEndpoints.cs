using System.Text.Json;
using Server.Core.Services;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Api.Endpoints;

public static class PluginEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/plugins");
    group.MapGet("/", GetAll);
    group.MapGet("/{id}", GetById);
    group.MapMethods("/{id}/config", ["OPTIONS"], GetConfigSchema);
    group.MapGet("/{id}/config", GetConfigValues);
    group.MapPut("/{id}/config", ApplyConfig);
    group.MapPost("/{id}/config/validate", ValidateField);
    group.MapPost("/{id}/start", Start);
    group.MapPost("/{id}/stop", Stop);
  }

  private static IResult GetAll(PluginService plugins, string? type = null)
  {
    var result = plugins.GetAll(type);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0010));
  }

  private static IResult GetById(string id, PluginService plugins)
  {
    var result = plugins.GetById(id);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0011));
  }

  private static IResult GetConfigSchema(string id, PluginService plugins)
  {
    var result = plugins.GetConfigSchema(id);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0014));
  }

  private static IResult GetConfigValues(string id, PluginService plugins)
  {
    var result = plugins.GetConfigValues(id);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0015));
  }

  private static IResult ApplyConfig(
    string id,
    Dictionary<string, JsonElement> values,
    PluginService plugins)
  {
    var converted = values.ToDictionary(
      kvp => kvp.Key,
      kvp => UnwrapJsonElement(kvp.Value));
    var result = plugins.ApplyConfigValues(id, converted);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0016));
  }

  private static object UnwrapJsonElement(JsonElement element) => element.ValueKind switch
  {
    JsonValueKind.String => element.GetString()!,
    JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    _ => element.ToString()
  };

  private static IResult ValidateField(
    string id,
    ValidateFieldRequest body,
    PluginService plugins)
  {
    var value = body.Value is JsonElement el ? UnwrapJsonElement(el) : body.Value;
    var result = plugins.ValidateField(id, body.Key, value);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0017));
  }

  private static async Task<IResult> Start(
    string id,
    PluginService plugins,
    CancellationToken ct)
  {
    var result = await plugins.UserStartAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0012));
  }

  private static async Task<IResult> Stop(
    string id,
    PluginService plugins,
    CancellationToken ct)
  {
    var result = await plugins.UserStopAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.PluginManagement, 0x0013));
  }
}
