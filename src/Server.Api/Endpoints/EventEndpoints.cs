using Server.Core.Services;
using Shared.Models;

namespace Server.Api.Endpoints;

public static class EventEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/events");
    group.MapGet("/", Query);
    group.MapGet("/{id:guid}", GetById);
  }

  private static async Task<IResult> Query(
    ulong from,
    ulong to,
    Guid? cameraId,
    string? type,
    int? limit,
    int? offset,
    EventService events,
    CancellationToken ct)
  {
    var result = await events.QueryAsync(
      cameraId, type, from, to,
      limit ?? 100, offset ?? 0, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.Events, 0x0010));
  }

  private static async Task<IResult> GetById(
    Guid id,
    EventService events,
    CancellationToken ct)
  {
    var result = await events.GetByIdAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.Events, 0x0011));
  }
}
