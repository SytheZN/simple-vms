using Server.Core.Services;
using Shared.Models;

namespace Server.Api.Endpoints;

public static class RecordingEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/recordings");
    group.MapGet("/{cameraId:guid}", GetSegments);
    group.MapGet("/{cameraId:guid}/timeline", GetTimeline);
  }

  private static async Task<IResult> GetSegments(
    Guid cameraId,
    ulong from,
    ulong to,
    string? profile,
    RecordingService recordings,
    CancellationToken ct)
  {
    var result = await recordings.GetSegmentsAsync(
      cameraId, profile ?? "main", from, to, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.Recording, 0x0010));
  }

  private static async Task<IResult> GetTimeline(
    Guid cameraId,
    ulong from,
    ulong to,
    string? profile,
    RecordingService recordings,
    CancellationToken ct)
  {
    var result = await recordings.GetTimelineAsync(
      cameraId, profile ?? "main", from, to, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.Recording, 0x0011));
  }
}
