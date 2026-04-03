using Server.Core.Services;
using Shared.Models;

namespace Server.Api.Endpoints;

public static class SnapshotEndpoint
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.MapGet("/api/v1/cameras/{id:guid}/snapshot", GetSnapshot);
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
