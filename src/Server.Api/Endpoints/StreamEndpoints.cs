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
      session.CancelCurrent();
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
          _currentTask = RunLiveAsync(live, opCts.Token);
          break;
        case ClientMessageType.Fetch:
          _currentTask = RunFetchAsync(fetch, opCts.Token);
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

  public void CancelCurrent()
  {
    var prev = _currentOp;
    _currentOp = null;
    if (prev != null)
    {
      prev.Cancel();
      prev.Dispose();
    }
  }

  private async Task RunLiveAsync(LiveMessage msg, CancellationToken ct)
  {
    await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Ack), ct);
    await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Live), ct);

    var pipeline = tapRegistry.GetPipeline(cameraId, msg.Profile);
    if (pipeline == null)
    {
      await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
      return;
    }

    var header = pipeline.VideoHeader;
    if (!header.IsEmpty)
      await SendAsync(StreamMessageWriter.SerializeInit(msg.Profile, header), ct);

    var subscribeResult = await pipeline.SubscribeVideoAsync(ct);
    if (subscribeResult.IsT1)
    {
      await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
      return;
    }

    var videoStream = subscribeResult.AsT0;
    var readMethod = videoStream.GetType().GetMethod("ReadAsync");
    if (readMethod == null) return;

    var enumerable = (IAsyncEnumerable<IDataUnit>)readMethod.Invoke(videoStream, [ct])!;
    var gopBuffer = new MemoryStream();
    var inGop = false;
    ulong gopTimestamp = 0;
    var frameCount = 0;

    var waitingForKeyframe = false;

    try
    {
      await foreach (var frame in enumerable.WithCancellation(ct))
      {
        if (webSocket.State != WebSocketState.Open)
          break;

        if (waitingForKeyframe && !frame.IsSyncPoint)
          continue;
        waitingForKeyframe = false;

        if (frame.IsSyncPoint)
        {
          if (inGop && gopBuffer.Length > 0)
          {
            await SendGopFragment(GopFlags.End, msg.Profile, gopTimestamp,
              gopBuffer.ToArray(), ct);
            gopBuffer.SetLength(0);
          }

          inGop = true;
          gopTimestamp = frame.Timestamp;
          frameCount = 0;
        }

        if (!inGop)
          continue;

        gopBuffer.Write(frame.Data.Span);
        frameCount++;

        if (frame.IsSyncPoint)
        {
          var start = System.Diagnostics.Stopwatch.GetTimestamp();
          await SendGopFragment(GopFlags.Begin, msg.Profile, gopTimestamp,
            gopBuffer.ToArray(), ct);
          var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
          if (elapsed.TotalMilliseconds > 500)
          {
            waitingForKeyframe = true;
            logger.LogDebug("Live stream client falling behind, skipping to next keyframe");
          }
          gopBuffer.SetLength(0);
        }
        else if (frameCount % 10 == 0 && gopBuffer.Length > 0)
        {
          await SendGopFragment(GopFlags.None, msg.Profile, gopTimestamp,
            gopBuffer.ToArray(), ct);
          gopBuffer.SetLength(0);
        }
      }
    }
    catch (OperationCanceledException) { }
  }

  private async Task RunFetchAsync(FetchMessage msg, CancellationToken ct)
  {
    await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Ack), ct);
    await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Recording), ct);

    var reverse = msg.From > msg.To;
    var rangeStart = reverse ? msg.To : msg.From;
    var rangeEnd = reverse ? msg.From : msg.To;

    var data = plugins.DataProvider;
    var storage = plugins.StorageProviders.FirstOrDefault();
    if (storage == null)
    {
      await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
      return;
    }

    var streamsResult = await data.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1)
    {
      await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
      return;
    }

    var stream = streamsResult.AsT0.FirstOrDefault(s => s.Profile == msg.Profile);
    if (stream == null)
    {
      await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
      return;
    }

    var pointResult = await data.Segments.FindPlaybackPointAsync(stream.Id, rangeStart, ct);
    if (pointResult.IsT1)
    {
      if (tapRegistry.GetPipeline(cameraId, msg.Profile) != null)
      {
        await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.FetchComplete), ct);
        return;
      }
      await RunLiveAsync(new LiveMessage(msg.Profile), ct);
      return;
    }

    if (pointResult.AsT0.KeyframeTimestamp > rangeStart + 1_000_000)
      await SendAsync(StreamMessageWriter.SerializeGap(rangeStart, pointResult.AsT0.KeyframeTimestamp), ct);

    var point = pointResult.AsT0;
    var segResult = await data.Segments.GetByIdAsync(point.SegmentId, ct);
    if (segResult.IsT1)
    {
      await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
      return;
    }

    var format = plugins.StreamFormats.FirstOrDefault(f => f.FormatId == stream.FormatId);
    if (format == null)
    {
      await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
      return;
    }

    var collectedGops = new List<(ulong Timestamp, byte[] Data)>();
    var initSent = false;
    var currentSegment = segResult.AsT0;
    var seekOffset = point.ByteOffset;
    ulong lastEndTime = 0;

    while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
    {
      Stream fileStream;
      try
      {
        fileStream = await storage.OpenReadAsync(currentSegment.SegmentRef, ct);
      }
      catch (FileNotFoundException)
      {
        await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
        return;
      }

      await using (fileStream)
      {
        var readerResult = format.CreateReader(fileStream);
        if (readerResult.IsT1)
        {
          await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.Error), ct);
          return;
        }

        await using var reader = readerResult.AsT0;

        if (!initSent)
        {
          var pipeline = tapRegistry.GetPipeline(cameraId, msg.Profile);
          var header = pipeline?.VideoHeader ?? ReadOnlyMemory<byte>.Empty;
          if (header.IsEmpty)
            header = ReadInitSegment(fileStream);
          if (!header.IsEmpty)
            await SendAsync(StreamMessageWriter.SerializeInit(msg.Profile, header), ct);
          initSent = true;
        }

        if (seekOffset >= 0)
          await reader.SeekAsync(seekOffset, ct);

        var gopStream = new MemoryStream();
        ulong gopTimestamp = 0;
        var gopStarted = false;

        await foreach (var frame in reader.ReadAsync(ct))
        {
          if (frame.Timestamp > rangeEnd)
            break;

          if (frame.IsSyncPoint)
          {
            if (gopStarted && gopStream.Length > 0)
            {
              var gopData = gopStream.ToArray();
              if (reverse)
                collectedGops.Add((gopTimestamp, gopData));
              else
                await SendGopFragment(GopFlags.Begin | GopFlags.End, msg.Profile,
                  gopTimestamp, gopData, ct);
              gopStream.SetLength(0);
            }
            gopTimestamp = frame.Timestamp;
            gopStarted = true;
          }

          if (!gopStarted)
            continue;

          gopStream.Write(frame.Data.Span);
        }

        if (gopStarted && gopStream.Length > 0)
        {
          var gopData = gopStream.ToArray();
          if (reverse)
            collectedGops.Add((gopTimestamp, gopData));
          else
            await SendGopFragment(GopFlags.Begin | GopFlags.End, msg.Profile,
              gopTimestamp, gopData, ct);
        }
      }

      lastEndTime = currentSegment.EndTime;

      var nextPoint = await data.Segments.FindPlaybackPointAsync(
        stream.Id, currentSegment.EndTime + 1, ct);
      if (nextPoint.IsT1 || nextPoint.AsT0.SegmentId == currentSegment.Id)
      {
        if (tapRegistry.GetPipeline(cameraId, msg.Profile) != null)
          break;
        await RunLiveAsync(new LiveMessage(msg.Profile), ct);
        return;
      }

      var nextSeg = await data.Segments.GetByIdAsync(nextPoint.AsT0.SegmentId, ct);
      if (nextSeg.IsT1)
      {
        if (tapRegistry.GetPipeline(cameraId, msg.Profile) != null)
          break;
        await RunLiveAsync(new LiveMessage(msg.Profile), ct);
        return;
      }

      currentSegment = nextSeg.AsT0;

      if (currentSegment.StartTime > rangeEnd)
        break;

      if (currentSegment.StartTime > lastEndTime + 1_000_000)
      {
        if (!reverse)
          await SendAsync(StreamMessageWriter.SerializeGap(lastEndTime, currentSegment.StartTime), ct);
      }

      seekOffset = -1;
    }

    if (reverse)
    {
      for (var i = collectedGops.Count - 1; i >= 0; i--)
      {
        if (webSocket.State != WebSocketState.Open || ct.IsCancellationRequested)
          break;
        var (ts, gopData) = collectedGops[i];
        await SendGopFragment(GopFlags.Begin | GopFlags.End, msg.Profile, ts, gopData, ct);
      }
    }

    if (webSocket.State == WebSocketState.Open)
      await SendAsync(StreamMessageWriter.SerializeStatus(StreamStatus.FetchComplete), ct);
  }

  private async Task SendGopFragment(GopFlags flags, string profile, ulong timestamp,
    byte[] data, CancellationToken ct)
  {
    var msg = StreamMessageWriter.SerializeGop(flags, profile, timestamp, data);
    await SendAsync(msg, ct);
  }

  private async Task SendAsync(byte[] data, CancellationToken ct)
  {
    if (webSocket.State != WebSocketState.Open) return;
    await webSocket.SendAsync(data, WebSocketMessageType.Binary, true, ct);
  }

  private static ReadOnlyMemory<byte> ReadInitSegment(Stream stream)
  {
    var savedPos = stream.Position;
    stream.Position = 0;
    var header = new byte[8];
    long initEnd = 0;

    while (true)
    {
      var read = stream.Read(header, 0, 8);
      if (read < 8) break;

      var size = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
      var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);

      if (type is "moof" or "mdat")
      {
        initEnd = stream.Position - 8;
        break;
      }

      if (size == 0) break;
      stream.Position = stream.Position - 8 + size;
    }

    if (initEnd <= 0)
    {
      stream.Position = savedPos;
      return ReadOnlyMemory<byte>.Empty;
    }

    var initBytes = new byte[initEnd];
    stream.Position = 0;
    stream.ReadExactly(initBytes);
    stream.Position = savedPos;
    return initBytes;
  }
}
