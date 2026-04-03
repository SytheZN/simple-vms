using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Server.Streaming;
using Shared.Models;
using Shared.Protocol;

namespace Server.Api.Endpoints;

public static class StreamEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.MapGet("/api/v1/stream/{cameraId:guid}", HandleStream);
  }

  private static async Task<IResult> HandleStream(
    Guid cameraId,
    HttpContext context,
    StreamTapRegistry tapRegistry,
    IPluginHost plugins,
    ILoggerFactory loggerFactory,
    CancellationToken ct)
  {
    if (!context.WebSockets.IsWebSocketRequest)
      return ApiResponse.Err(Error.Create(
        ModuleIds.ApiWebSocketStream, 0x0010, Result.BadRequest,
        "WebSocket upgrade required"));

    var logger = loggerFactory.CreateLogger("StreamEndpoint");
    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

    var session = new StreamSession(cameraId, webSocket, tapRegistry, plugins, logger);
    try
    {
      await session.RunAsync(ct);
    }
    catch (WebSocketException) { }
    catch (OperationCanceledException) { }
    finally
    {
      await session.CancelCurrentAsync();
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
  }
}

internal sealed class WebSocketStreamSink(WebSocket webSocket) : IStreamSink
{
  public bool IsOpen => webSocket.State == WebSocketState.Open;

  public Task SendInitAsync(string profile, ReadOnlyMemory<byte> data, CancellationToken ct) =>
    SendAsync(StreamMessageWriter.SerializeInit(profile, data), ct);

  public Task SendGopAsync(GopFlags flags, string profile, ulong timestamp, ReadOnlyMemory<byte> data, CancellationToken ct) =>
    SendAsync(StreamMessageWriter.SerializeGop(flags, profile, timestamp, data), ct);

  public Task SendStatusAsync(StreamStatus status, CancellationToken ct) =>
    SendAsync(StreamMessageWriter.SerializeStatus(status), ct);

  public Task SendGapAsync(ulong from, ulong to, CancellationToken ct) =>
    SendAsync(StreamMessageWriter.SerializeGap(from, to), ct);

  private async Task SendAsync(byte[] data, CancellationToken ct)
  {
    if (webSocket.State != WebSocketState.Open) return;
    await webSocket.SendAsync(data, WebSocketMessageType.Binary, true, ct);
  }
}

internal sealed class StreamSession(
  Guid cameraId,
  WebSocket webSocket,
  StreamTapRegistry tapRegistry,
  IPluginHost plugins,
  ILogger logger)
{
  private CancellationTokenSource? _currentOp;
  private Task? _currentTask;

  public async Task RunAsync(CancellationToken ct)
  {
    var buf = new byte[64];
    var sink = new WebSocketStreamSink(webSocket);

    while (webSocket.State == WebSocketState.Open)
    {
      var result = await webSocket.ReceiveAsync(buf, ct);
      if (result.MessageType == WebSocketMessageType.Close)
        break;
      if (result.Count == 0)
        continue;

      var data = buf.AsSpan(0, result.Count);
      var type = StreamMessageReader.ReadType(data);
      var live = type == ClientMessageType.Live ? StreamMessageReader.ReadLive(data) : default;
      var fetch = type == ClientMessageType.Fetch ? StreamMessageReader.ReadFetch(data) : default;

      await CancelCurrentAsync();
      var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      _currentOp = opCts;

      switch (type)
      {
        case ClientMessageType.Live:
          _currentTask = StreamSessionRunner.RunLiveAsync(
            cameraId, live.Profile, sink, tapRegistry, logger, opCts.Token);
          break;
        case ClientMessageType.Fetch:
          _currentTask = StreamSessionRunner.RunFetchAsync(
            cameraId, fetch.Profile, fetch.From, fetch.To, sink, tapRegistry, plugins, logger, opCts.Token);
          break;
      }
    }
  }

  public async Task CancelCurrentAsync()
  {
    var prev = _currentOp;
    var prevTask = _currentTask;
    _currentOp = null;
    _currentTask = null;
    if (prev != null)
    {
      prev.Cancel();
      if (prevTask != null)
      {
        try { await prevTask; }
        catch (OperationCanceledException) { }
        catch (Exception) { }
      }
      prev.Dispose();
    }
  }

}
