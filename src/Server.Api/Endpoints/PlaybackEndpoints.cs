using System.Net.WebSockets;
using Shared.Models;

namespace Server.Api.Endpoints;

public static class PlaybackEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.MapGet("/api/v1/playback/{cameraId:guid}/{profile}", HandlePlayback);
  }

  private static Task<IResult> HandlePlayback(
    Guid cameraId,
    string profile,
    ulong from,
    ulong? to,
    HttpContext context,
    CancellationToken ct)
  {
    if (!context.WebSockets.IsWebSocketRequest)
      return Task.FromResult(ApiResponse.Err(Error.Create(
        ModuleIds.LiveStreaming, 0x0010, Result.BadRequest,
        "WebSocket upgrade required")));

    return Task.FromResult(ApiResponse.Err(Error.Create(
      ModuleIds.LiveStreaming, 0x0011, Result.Unavailable,
      "Playback is not yet available (recording pipeline not implemented)")));
  }
}
