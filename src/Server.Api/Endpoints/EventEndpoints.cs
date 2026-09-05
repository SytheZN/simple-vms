using System.Net.WebSockets;
using System.Text.Json;
using Server.Core;
using Server.Core.Events;
using Shared.Api;
using Shared.Models;
using Shared.Protocol;

namespace Server.Api.Endpoints;

public static class EventEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.MapGet("/api/v1/events/stream", HandleEvents);
  }

  private static async Task<IResult> HandleEvents(
    HttpContext context,
    IEventBus eventBus,
    CancellationToken ct)
  {
    if (!context.WebSockets.IsWebSocketRequest)
      return ApiResponse.Err(Error.Create(
        ModuleIds.ApiWebSocketEvents, 0x0001, Result.BadRequest,
        "WebSocket upgrade required"));

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    var closed = WatchForCloseAsync(webSocket, cts);

    try
    {
      await CameraEventFeed.RunAsync(SendAsync, eventBus, cts.Token);
    }
    catch (WebSocketException) { }
    catch (OperationCanceledException) { }
    finally
    {
      cts.Cancel();
      await closed;
      if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
      {
        try
        {
          await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch { }
      }
    }

    return Results.Empty;

    async Task SendAsync(
      EventChannelMessage message, EventChannelFlags flags, CancellationToken token)
    {
      if (webSocket.State != WebSocketState.Open) return;

      var payload = JsonSerializer.SerializeToUtf8Bytes(
        new LiveEventDto
        {
          Id = message.Id,
          CameraId = message.CameraId,
          Type = message.Type,
          StartTime = message.StartTime,
          EndTime = message.EndTime,
          Metadata = message.Metadata,
          Ended = (flags & EventChannelFlags.End) != 0
        },
        ServerJsonContext.Default.LiveEventDto);

      await webSocket.SendAsync(payload, WebSocketMessageType.Text, true, token);
    }
  }

  private static async Task WatchForCloseAsync(
    WebSocket webSocket, CancellationTokenSource cts)
  {
    var buffer = new byte[8];
    try
    {
      while (webSocket.State == WebSocketState.Open)
      {
        var result = await webSocket.ReceiveAsync(buffer, cts.Token);
        if (result.MessageType == WebSocketMessageType.Close)
          break;
      }
    }
    catch (OperationCanceledException) { }
    catch (WebSocketException) { }
    finally
    {
      await cts.CancelAsync();
    }
  }
}
