using Server.Core.Services;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Api.Endpoints;

public static class CameraEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/cameras");
    group.MapGet("/", GetAll);
    group.MapPost("/", Create);
    group.MapGet("/{id:guid}", GetById);
    group.MapPut("/{id:guid}", Update);
    group.MapDelete("/{id:guid}", Delete);
    group.MapPost("/{id:guid}/restart", Restart);
    group.MapGet("/{id:guid}/snapshot", GetSnapshot);
  }

  private static async Task<IResult> GetAll(
    CameraService cameras,
    string? status,
    CancellationToken ct)
  {
    var result = await cameras.GetAllAsync(status, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0010));
  }

  private static async Task<IResult> Create(
    CreateCameraRequest request,
    CameraService cameras,
    CancellationToken ct)
  {
    var result = await cameras.CreateAsync(request, ct);
    return ApiResponse.Created(result, new DebugTag(ModuleIds.CameraManagement, 0x0011));
  }

  private static async Task<IResult> GetById(
    Guid id,
    CameraService cameras,
    CancellationToken ct)
  {
    var result = await cameras.GetByIdAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0012));
  }

  private static async Task<IResult> Update(
    Guid id,
    UpdateCameraRequest request,
    CameraService cameras,
    CancellationToken ct)
  {
    var result = await cameras.UpdateAsync(id, request, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0013));
  }

  private static async Task<IResult> Delete(
    Guid id,
    CameraService cameras,
    CancellationToken ct)
  {
    var result = await cameras.DeleteAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0014));
  }

  private static async Task<IResult> Restart(
    Guid id,
    CameraService cameras,
    CancellationToken ct)
  {
    var result = await cameras.RestartAsync(id, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.CameraManagement, 0x0015));
  }

  private static async Task<IResult> GetSnapshot(
    Guid id,
    CameraService cameras,
    CancellationToken ct)
  {
    var result = await cameras.GetSnapshotAsync(id, ct);
    return result.Match<IResult>(
      bytes => Results.Bytes(bytes, "image/jpeg"),
      error => ApiResponse.Err(error));
  }
}
