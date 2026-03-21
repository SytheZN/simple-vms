using System.Diagnostics;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Server.Streaming;
using Shared.Models;

namespace Server.Api.Endpoints;

public static class LiveStreamEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.MapGet("/api/v1/live/{cameraId:guid}/{profile}", HandleLiveStream);
    app.MapMethods("/api/v1/live/{cameraId:guid}/{profile}", ["OPTIONS"], GetStreamMetadata);
  }

  private static IResult GetStreamMetadata(
    Guid cameraId,
    string profile,
    StreamTapRegistry tapRegistry)
  {
    var pipeline = tapRegistry.GetPipeline(cameraId, profile);
    if (pipeline == null)
      return ApiResponse.Err(Error.Create(
        ModuleIds.LiveStreaming, 0x0010, Result.NotFound,
        $"No pipeline for camera {cameraId} profile '{profile}'"));

    var info = pipeline.VideoInfo;
    if (info == null)
      return ApiResponse.Err(Error.Create(
        ModuleIds.LiveStreaming, 0x0011, Result.Unavailable,
        "Stream metadata not available (pipeline not initialized)"));

    return Results.Json(new ResponseEnvelope
    {
      Result = Result.Success,
      DebugTag = new DebugTag(ModuleIds.LiveStreaming, 0x0012),
      Body = new { info.MimeType, info.Resolution }
    }, ApiResponse.SerializerOptions);
  }

  private static async Task<IResult> HandleLiveStream(
    Guid cameraId,
    string profile,
    HttpContext context,
    StreamTapRegistry tapRegistry,
    ILoggerFactory loggerFactory,
    CancellationToken ct)
  {
    if (!context.WebSockets.IsWebSocketRequest)
      return ApiResponse.Err(Error.Create(
        ModuleIds.LiveStreaming, 0x0001, Result.BadRequest,
        "WebSocket upgrade required"));

    var pipeline = tapRegistry.GetPipeline(cameraId, profile);
    if (pipeline == null)
      return ApiResponse.Err(Error.Create(
        ModuleIds.LiveStreaming, 0x0002, Result.NotFound,
        $"No pipeline for camera {cameraId} profile '{profile}'"));

    var header = pipeline.VideoHeader;
    var logger = loggerFactory.CreateLogger("LiveStreaming");

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

    try
    {
      if (!header.IsEmpty)
        await webSocket.SendAsync(header, WebSocketMessageType.Binary, true, ct);

      var subscribeResult = await pipeline.SubscribeVideoAsync(ct);
      if (subscribeResult.IsT1)
        return ApiResponse.Err(subscribeResult.AsT1);

      var videoStream = subscribeResult.AsT0;
      await StreamToWebSocketAsync(videoStream, webSocket, logger, ct);
    }
    catch (WebSocketException) { }
    catch (OperationCanceledException) { }
    finally
    {
      if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
      {
        try
        {
          await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure, null,
            CancellationToken.None);
        }
        catch { }
      }
    }

    return Results.Empty;
  }

  private static async Task StreamToWebSocketAsync(
    IVideoStream videoStream,
    WebSocket webSocket,
    ILogger logger,
    CancellationToken ct)
  {
    using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var closeTask = WaitForCloseAsync(webSocket, closeCts.Token);

    var readMethod = videoStream.GetType().GetMethod("ReadAsync");
    if (readMethod == null) return;

    var enumerable = (IAsyncEnumerable<IDataUnit>)readMethod.Invoke(videoStream, [closeCts.Token])!;
    bool waitingForKeyframe = false;

    try
    {
      await foreach (var frame in enumerable.WithCancellation(closeCts.Token))
      {
        if (webSocket.State != WebSocketState.Open)
          break;

        if (waitingForKeyframe && !frame.IsSyncPoint)
          continue;
        waitingForKeyframe = false;

        var start = Stopwatch.GetTimestamp();
        await webSocket.SendAsync(
          frame.Data, WebSocketMessageType.Binary, true, closeCts.Token);
        var elapsed = Stopwatch.GetElapsedTime(start);

        if (elapsed.TotalMilliseconds > 500)
        {
          waitingForKeyframe = true;
          logger.LogDebug("Live stream client falling behind, skipping to next keyframe");
        }
      }
    }
    catch (OperationCanceledException) { }

    closeCts.Cancel();
    try { await closeTask; } catch { }
  }

  private static async Task WaitForCloseAsync(WebSocket webSocket, CancellationToken ct)
  {
    var buf = new byte[128];
    try
    {
      var result = await webSocket.ReceiveAsync(buf, ct);
      if (result.MessageType == WebSocketMessageType.Close)
        return;
    }
    catch { }
  }
}
