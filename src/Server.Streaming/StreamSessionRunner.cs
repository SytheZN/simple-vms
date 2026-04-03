using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;
using Shared.Protocol;

namespace Server.Streaming;

public interface IStreamSink
{
  bool IsOpen { get; }
  Task SendInitAsync(string profile, ReadOnlyMemory<byte> data, CancellationToken ct);
  Task SendGopAsync(GopFlags flags, string profile, ulong timestamp, ReadOnlyMemory<byte> data, CancellationToken ct);
  Task SendStatusAsync(StreamStatus status, CancellationToken ct);
  Task SendGapAsync(ulong from, ulong to, CancellationToken ct);
}

public static class StreamSessionRunner
{
  public static async Task RunLiveAsync(
    Guid cameraId,
    string profile,
    IStreamSink sink,
    StreamTapRegistry tapRegistry,
    ILogger logger,
    CancellationToken ct)
  {
    await sink.SendStatusAsync(StreamStatus.Ack, ct);
    await sink.SendStatusAsync(StreamStatus.Live, ct);
    await RunLiveCoreAsync(cameraId, profile, sink, tapRegistry, logger, ct);
  }

  internal static async Task RunLiveCoreAsync(
    Guid cameraId,
    string profile,
    IStreamSink sink,
    StreamTapRegistry tapRegistry,
    ILogger logger,
    CancellationToken ct)
  {
    var pipeline = tapRegistry.GetPipeline(cameraId, profile);
    if (pipeline == null)
    {
      await sink.SendStatusAsync(StreamStatus.Error, ct);
      return;
    }

    var header = pipeline.VideoHeader;
    if (!header.IsEmpty)
      await sink.SendInitAsync(profile, header, ct);

    var subscribeResult = await pipeline.SubscribeVideoAsync(ct);
    if (subscribeResult.IsT1)
    {
      await sink.SendStatusAsync(StreamStatus.Error, ct);
      return;
    }

    var videoStream = subscribeResult.AsT0;
    await using var _ = videoStream as IAsyncDisposable;
    var enumerable = videoStream.ReadAsync(ct);
    using var gopBuffer = new MemoryStream();
    var inGop = false;
    ulong gopTimestamp = 0;
    var frameCount = 0;
    var waitingForKeyframe = false;

    try
    {
      await foreach (var frame in enumerable.WithCancellation(ct))
      {
        if (!sink.IsOpen)
          break;

        if (waitingForKeyframe && !frame.IsSyncPoint)
          continue;
        waitingForKeyframe = false;

        if (frame.IsSyncPoint)
        {
          if (inGop && gopBuffer.Length > 0)
          {
            await sink.SendGopAsync(GopFlags.End, profile, gopTimestamp,
              gopBuffer.GetBuffer().AsMemory(0, (int)gopBuffer.Length), ct);
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
          await sink.SendGopAsync(GopFlags.Begin, profile, gopTimestamp,
            gopBuffer.GetBuffer().AsMemory(0, (int)gopBuffer.Length), ct);
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
          await sink.SendGopAsync(GopFlags.None, profile, gopTimestamp,
            gopBuffer.GetBuffer().AsMemory(0, (int)gopBuffer.Length), ct);
          gopBuffer.SetLength(0);
        }
      }
    }
    catch (OperationCanceledException) { }
  }

  public static async Task RunFetchAsync(
    Guid cameraId,
    string profile,
    ulong from,
    ulong to,
    IStreamSink sink,
    StreamTapRegistry tapRegistry,
    IPluginHost plugins,
    ILogger logger,
    CancellationToken ct)
  {
    await sink.SendStatusAsync(StreamStatus.Ack, ct);
    await sink.SendStatusAsync(StreamStatus.Recording, ct);

    var reverse = from > to;
    var rangeStart = reverse ? to : from;
    var rangeEnd = reverse ? from : to;

    var data = plugins.DataProvider;
    var storage = plugins.StorageProviders.FirstOrDefault();
    if (storage == null)
    {
      await sink.SendStatusAsync(StreamStatus.Error, ct);
      return;
    }

    var streamsResult = await data.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1)
    {
      await sink.SendStatusAsync(StreamStatus.Error, ct);
      return;
    }

    var stream = streamsResult.AsT0.FirstOrDefault(s => s.Profile == profile);
    if (stream == null)
    {
      await sink.SendStatusAsync(StreamStatus.Error, ct);
      return;
    }

    var pointResult = await data.Segments.FindPlaybackPointAsync(stream.Id, rangeStart, ct);
    if (pointResult.IsT1)
    {
      await sink.SendStatusAsync(StreamStatus.FetchComplete, ct);
      return;
    }

    if (pointResult.AsT0.KeyframeTimestamp > rangeStart + 1_000_000)
      await sink.SendGapAsync(rangeStart, pointResult.AsT0.KeyframeTimestamp, ct);

    var point = pointResult.AsT0;
    var segResult = await data.Segments.GetByIdAsync(point.SegmentId, ct);
    if (segResult.IsT1)
    {
      await sink.SendStatusAsync(StreamStatus.Error, ct);
      return;
    }

    var format = plugins.StreamFormats.FirstOrDefault(f => f.FormatId == stream.FormatId);
    if (format == null)
    {
      await sink.SendStatusAsync(StreamStatus.Error, ct);
      return;
    }

    const long maxReverseBufferBytes = 256 * 1024 * 1024;
    long reverseBufferBytes = 0;
    var collectedGops = new List<(ulong Timestamp, byte[] Data)>();
    var initSent = false;
    var currentSegment = segResult.AsT0;
    var seekOffset = point.ByteOffset;
    ulong lastEndTime = 0;

    var gopStream = new MemoryStream();

    while (sink.IsOpen && !ct.IsCancellationRequested)
    {
      Stream fileStream;
      try
      {
        fileStream = await storage.OpenReadAsync(currentSegment.SegmentRef, ct);
      }
      catch (FileNotFoundException)
      {
        await sink.SendStatusAsync(StreamStatus.Error, ct);
        return;
      }

      await using (fileStream)
      {
        if (!initSent)
        {
          var pipeline = tapRegistry.GetPipeline(cameraId, profile);
          var header = pipeline?.VideoHeader ?? ReadOnlyMemory<byte>.Empty;
          if (header.IsEmpty)
            header = await ReadInitSegmentAsync(fileStream, ct);
          if (!header.IsEmpty)
            await sink.SendInitAsync(profile, header, ct);
          initSent = true;
        }

        var readerResult = format.CreateReader(fileStream);
        if (readerResult.IsT1)
        {
          await sink.SendStatusAsync(StreamStatus.Error, ct);
          return;
        }

        await using var reader = readerResult.AsT0;

        if (seekOffset >= 0)
          await reader.SeekAsync(seekOffset, ct);

        ulong gopTimestamp = 0;
        var gopStarted = false;
        gopStream.SetLength(0);

        await foreach (var frame in reader.ReadAsync(ct))
        {
          if (frame.Timestamp > rangeEnd)
            break;

          if (frame.IsSyncPoint)
          {
            if (gopStarted && gopStream.Length > 0)
            {
              if (reverse)
              {
                var gopData = gopStream.ToArray();
                reverseBufferBytes += gopData.Length;
                if (reverseBufferBytes > maxReverseBufferBytes)
                {
                  await sink.SendStatusAsync(StreamStatus.Error, ct);
                  return;
                }
                collectedGops.Add((gopTimestamp, gopData));
              }
              else
                await sink.SendGopAsync(GopFlags.Begin | GopFlags.End, profile,
                  gopTimestamp, gopStream.GetBuffer().AsMemory(0, (int)gopStream.Length), ct);
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
          if (reverse)
          {
            var gopData = gopStream.ToArray();
            reverseBufferBytes += gopData.Length;
            if (reverseBufferBytes > maxReverseBufferBytes)
            {
              await sink.SendStatusAsync(StreamStatus.Error, ct);
              return;
            }
            collectedGops.Add((gopTimestamp, gopData));
          }
          else
            await sink.SendGopAsync(GopFlags.Begin | GopFlags.End, profile,
              gopTimestamp, gopStream.GetBuffer().AsMemory(0, (int)gopStream.Length), ct);
        }
      }

      lastEndTime = currentSegment.EndTime;

      var nextPoint = await data.Segments.FindPlaybackPointAsync(
        stream.Id, currentSegment.EndTime + 1, ct);
      if (nextPoint.IsT1 || nextPoint.AsT0.SegmentId == currentSegment.Id)
        break;

      var nextSeg = await data.Segments.GetByIdAsync(nextPoint.AsT0.SegmentId, ct);
      if (nextSeg.IsT1)
        break;

      currentSegment = nextSeg.AsT0;

      if (currentSegment.StartTime > rangeEnd)
        break;

      if (currentSegment.StartTime > lastEndTime + 1_000_000)
      {
        if (!reverse)
          await sink.SendGapAsync(lastEndTime, currentSegment.StartTime, ct);
      }

      seekOffset = -1;
    }

    if (reverse)
    {
      for (var i = collectedGops.Count - 1; i >= 0; i--)
      {
        if (!sink.IsOpen || ct.IsCancellationRequested)
          break;
        var (ts, gopData) = collectedGops[i];
        await sink.SendGopAsync(GopFlags.Begin | GopFlags.End, profile, ts, gopData, ct);
      }
    }

    if (sink.IsOpen)
      await sink.SendStatusAsync(StreamStatus.FetchComplete, ct);
  }

  internal static async Task<ReadOnlyMemory<byte>> ReadInitSegmentAsync(
    Stream stream, CancellationToken ct)
  {
    var savedPos = stream.Position;
    stream.Position = 0;
    var header = new byte[8];
    long initEnd = 0;

    while (true)
    {
      var read = await stream.ReadAsync(header.AsMemory(0, 8), ct);
      if (read < 8) break;

      var size = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
      var type = header.AsSpan(4, 4);

      if (type.SequenceEqual("moof"u8) || type.SequenceEqual("mdat"u8))
      {
        initEnd = stream.Position - 8;
        break;
      }

      if (size < 8) break;
      stream.Position = stream.Position - 8 + size;
    }

    if (initEnd <= 0)
    {
      stream.Position = savedPos;
      return ReadOnlyMemory<byte>.Empty;
    }

    var initBytes = new byte[initEnd];
    stream.Position = 0;
    await stream.ReadExactlyAsync(initBytes, ct);
    stream.Position = savedPos;
    return initBytes;
  }
}
