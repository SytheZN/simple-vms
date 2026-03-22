using System.Buffers.Binary;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Server.Streaming;
using Shared.Models;

namespace Server.Api.Endpoints;

public static class PlaybackEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.MapMethods("/api/v1/playback/{cameraId:guid}/{profile}", ["OPTIONS"], GetPlaybackMetadata);
    app.MapGet("/api/v1/playback/{cameraId:guid}/{profile}", HandlePlayback);
  }

  private static async Task<IResult> GetPlaybackMetadata(
    Guid cameraId,
    string profile,
    ulong from,
    IPluginHost plugins,
    StreamTapRegistry tapRegistry,
    CancellationToken ct)
  {
    var data = plugins.DataProvider;

    var streamsResult = await data.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1)
      return ApiResponse.Err(streamsResult.AsT1);

    var stream = streamsResult.AsT0.FirstOrDefault(s => s.Profile == profile);
    if (stream == null)
      return ApiResponse.Err(Error.Create(
        ModuleIds.Recording, 0x0020, Result.NotFound,
        $"No stream with profile '{profile}' for camera {cameraId}"));

    var pipeline = tapRegistry.GetPipeline(cameraId, profile);
    var mimeType = pipeline?.VideoInfo?.MimeType ?? "video/mp4";
    var resolution = pipeline?.VideoInfo?.Resolution ?? "";

    var pointResult = await data.Segments.FindPlaybackPointAsync(stream.Id, from, ct);
    if (pointResult.IsT1)
      return Results.Json(new ResponseEnvelope
      {
        Result = Result.Success,
        DebugTag = new DebugTag(ModuleIds.Recording, 0x0021),
        Body = new { from = 0UL, mimeType, resolution }
      }, ApiResponse.SerializerOptions);

    var point = pointResult.AsT0;
    return Results.Json(new ResponseEnvelope
    {
      Result = Result.Success,
      DebugTag = new DebugTag(ModuleIds.Recording, 0x0022),
      Body = new
      {
        from = point.KeyframeTimestamp,
        segmentId = point.SegmentId,
        mimeType,
        resolution
      }
    }, ApiResponse.SerializerOptions);
  }

  private static async Task<IResult> HandlePlayback(
    Guid cameraId,
    string profile,
    ulong from,
    Guid segmentId,
    HttpContext context,
    IPluginHost plugins,
    CancellationToken ct)
  {
    if (!context.WebSockets.IsWebSocketRequest)
      return ApiResponse.Err(Error.Create(
        ModuleIds.Recording, 0x0010, Result.BadRequest,
        "WebSocket upgrade required"));

    var data = plugins.DataProvider;
    var storage = plugins.StorageProviders.FirstOrDefault();
    if (storage == null)
      return ApiResponse.Err(Error.Create(
        ModuleIds.Recording, 0x0011, Result.Unavailable,
        "No storage provider available"));

    var streamsResult = await data.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1)
      return ApiResponse.Err(streamsResult.AsT1);

    var stream = streamsResult.AsT0.FirstOrDefault(s => s.Profile == profile);
    if (stream == null)
      return ApiResponse.Err(Error.Create(
        ModuleIds.Recording, 0x0012, Result.NotFound,
        $"No stream with profile '{profile}' for camera {cameraId}"));

    var kfResult = await data.Keyframes.GetNearestAsync(segmentId, from, ct);
    if (kfResult.IsT1)
      return ApiResponse.Err(kfResult.AsT1);

    var segResult = await data.Segments.GetByIdAsync(segmentId, ct);
    if (segResult.IsT1)
      return ApiResponse.Err(segResult.AsT1);

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

    try
    {
      var currentSegment = segResult.AsT0;
      var seekOffset = kfResult.AsT0.ByteOffset;
      var firstSegment = true;

      while (webSocket.State == WebSocketState.Open)
      {
        await StreamSegmentAsync(
          storage, currentSegment.SegmentRef, seekOffset, webSocket, ct, sendInit: firstSegment);
        firstSegment = false;

        if (webSocket.State != WebSocketState.Open)
          break;

        var nextPoint = await data.Segments.FindPlaybackPointAsync(
          stream.Id, currentSegment.EndTime + 1, ct);
        if (nextPoint.IsT1 || nextPoint.AsT0.SegmentId == currentSegment.Id)
          break;

        currentSegment = (await data.Segments.GetByIdAsync(nextPoint.AsT0.SegmentId, ct))
          .Match(s => s, _ => (Segment?)null)!;
        if (currentSegment == null)
          break;
        seekOffset = -1;
      }

      if (webSocket.State == WebSocketState.Open)
      {
        await webSocket.CloseAsync(
          WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
      }
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
            WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch { }
      }
    }

    return Results.Empty;
  }

  private static async Task StreamSegmentAsync(
    IStorageProvider storage, string segmentRef, long seekOffset,
    WebSocket webSocket, CancellationToken ct, bool sendInit = true)
  {
    var fileStream = await storage.OpenReadAsync(segmentRef, ct);
    await using (fileStream)
    {
      var initSize = await ReadInitSizeAsync(fileStream, ct);
      if (initSize <= 0)
        return;

      if (sendInit)
      {
        var initBytes = new byte[initSize];
        fileStream.Position = 0;
        await fileStream.ReadExactlyAsync(initBytes, ct);
        await webSocket.SendAsync(initBytes, WebSocketMessageType.Binary, true, ct);
      }

      fileStream.Position = seekOffset >= 0 ? seekOffset : initSize;

      var boxHeader = new byte[8];
      var remaining = await WaitForRequestAsync(webSocket, ct);

      while (remaining > 0 && webSocket.State == WebSocketState.Open && fileStream.Position < fileStream.Length)
      {
        var headerRead = await fileStream.ReadAsync(boxHeader.AsMemory(0, 8), ct);
        if (headerRead < 8)
          break;

        var boxSize = (long)BinaryPrimitives.ReadUInt32BigEndian(boxHeader);
        if (boxSize < 8)
          break;

        var boxData = new byte[boxSize];
        boxHeader.CopyTo(boxData, 0);
        await fileStream.ReadExactlyAsync(boxData.AsMemory(8, (int)boxSize - 8), ct);
        await webSocket.SendAsync(boxData, WebSocketMessageType.Binary, true, ct);

        remaining--;
        if (remaining == 0 && fileStream.Position < fileStream.Length)
          remaining = await WaitForRequestAsync(webSocket, ct);
      }
    }
  }

  private static async Task<long> ReadInitSizeAsync(Stream stream, CancellationToken ct)
  {
    var header = new byte[8];
    long position = 0;

    while (true)
    {
      stream.Position = position;
      var read = await stream.ReadAsync(header, ct);
      if (read < 8)
        return -1;

      var boxSize = (long)BinaryPrimitives.ReadUInt32BigEndian(header);
      var boxType = System.Text.Encoding.ASCII.GetString(header, 4, 4);

      if (boxType == "moof" || boxType == "mdat")
        return position;

      if (boxSize == 0)
        return -1;

      position += boxSize;
    }
  }

  private static async Task<int> WaitForRequestAsync(WebSocket webSocket, CancellationToken ct)
  {
    var buf = new byte[16];
    try
    {
      var result = await webSocket.ReceiveAsync(buf, ct);
      if (result.MessageType == WebSocketMessageType.Close)
        return 0;
      if (result.Count >= 2)
        return BinaryPrimitives.ReadUInt16BigEndian(buf);
      if (result.Count > 0)
        return buf[0];
      return 0;
    }
    catch
    {
      return 0;
    }
  }
}
