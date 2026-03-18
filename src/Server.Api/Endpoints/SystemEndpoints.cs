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
    group.MapPost("/certs", GenerateCerts);
  }

  private static IResult GetHealth(SystemService system)
  {
    var health = system.GetHealth();
    return Results.Json(new ResponseEnvelope
    {
      Result = Result.Success,
      DebugTag = new DebugTag(ModuleIds.SystemManagement, 0x0010),
      Body = health
    }, ApiResponse.SerializerOptions);
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

  private static IResult GenerateCerts(ICertificateService certs)
  {
    if (certs.HasCerts)
      return ApiResponse.Err(new Error(
        Result.Conflict,
        new DebugTag(ModuleIds.SystemManagement, 0x0014),
        "Certificates already exist. To regenerate, delete the existing certificates first."));

    certs.GenerateCerts();
    return Results.Json(new ResponseEnvelope
    {
      Result = Result.Success,
      DebugTag = new DebugTag(ModuleIds.SystemManagement, 0x0015)
    }, ApiResponse.SerializerOptions);
  }
}
