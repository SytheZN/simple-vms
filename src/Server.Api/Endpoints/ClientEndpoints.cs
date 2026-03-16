using Server.Core.Services;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Api.Endpoints;

public static class ClientEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/clients");
    group.MapGet("/", GetAll);
    group.MapGet("/{id:guid}", GetById);
    group.MapPut("/{id:guid}", Update);
    group.MapDelete("/{id:guid}", Revoke);
  }

  private static async Task<IResult> GetAll(
    ClientService clients,
    CancellationToken ct)
  {
    var result = await clients.GetAllAsync(ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.ClientManagement, 0x0010));
  }

  private static async Task<IResult> GetById(
    Guid id,
    ClientService clients,
    CancellationToken ct)
  {
    var result = await clients.GetByIdAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.ClientManagement, 0x0011));
  }

  private static async Task<IResult> Update(
    Guid id,
    UpdateClientRequest request,
    ClientService clients,
    CancellationToken ct)
  {
    var result = await clients.UpdateAsync(id, request, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.ClientManagement, 0x0012));
  }

  private static async Task<IResult> Revoke(
    Guid id,
    ClientService clients,
    CancellationToken ct)
  {
    var result = await clients.RevokeAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.ClientManagement, 0x0013));
  }
}
