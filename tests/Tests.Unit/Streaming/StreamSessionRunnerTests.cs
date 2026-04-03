using Microsoft.Extensions.Logging.Abstractions;
using Server.Streaming;
using Shared.Models;
using Shared.Protocol;

namespace Tests.Unit.Streaming;

[TestFixture]
public class StreamSessionRunnerReadInitTests
{
  /// <summary>
  /// SCENARIO:
  /// Stream contains ftyp(8 bytes) + moov(16 bytes) + moof(8 bytes) + mdat
  ///
  /// ACTION:
  /// ReadInitSegment
  ///
  /// EXPECTED RESULT:
  /// Returns ftyp + moov (24 bytes), stops before moof
  /// </summary>
  [Test]
  public async Task ReadInitSegment_ReturnsBytesBeforeMoof()
  {
    var ms = new MemoryStream();
    WriteBox(ms, "ftyp", 8);
    WriteBox(ms, "moov", 16);
    WriteBox(ms, "moof", 8);
    WriteBox(ms, "mdat", 100);
    ms.Position = 50;

    var result = await StreamSessionRunner.ReadInitSegmentAsync(ms, CancellationToken.None);

    Assert.That(result.Length, Is.EqualTo(24));
    Assert.That(ms.Position, Is.EqualTo(50));
  }

  /// <summary>
  /// SCENARIO:
  /// Stream contains moof as the first box (no init segment)
  ///
  /// ACTION:
  /// ReadInitSegment
  ///
  /// EXPECTED RESULT:
  /// Returns empty (initEnd = 0)
  /// </summary>
  [Test]
  public async Task ReadInitSegment_MoofFirst_ReturnsEmpty()
  {
    var ms = new MemoryStream();
    WriteBox(ms, "moof", 8);
    WriteBox(ms, "mdat", 100);
    ms.Position = 0;

    var result = await StreamSessionRunner.ReadInitSegmentAsync(ms, CancellationToken.None);

    Assert.That(result.IsEmpty, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Stream contains mdat before moof
  ///
  /// ACTION:
  /// ReadInitSegment
  ///
  /// EXPECTED RESULT:
  /// Returns empty (mdat triggers the same stop as moof)
  /// </summary>
  [Test]
  public async Task ReadInitSegment_MdatFirst_ReturnsEmpty()
  {
    var ms = new MemoryStream();
    WriteBox(ms, "mdat", 100);
    ms.Position = 0;

    var result = await StreamSessionRunner.ReadInitSegmentAsync(ms, CancellationToken.None);

    Assert.That(result.IsEmpty, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Stream is empty (no boxes)
  ///
  /// ACTION:
  /// ReadInitSegment
  ///
  /// EXPECTED RESULT:
  /// Returns empty
  /// </summary>
  [Test]
  public async Task ReadInitSegment_EmptyStream_ReturnsEmpty()
  {
    var ms = new MemoryStream();

    var result = await StreamSessionRunner.ReadInitSegmentAsync(ms, CancellationToken.None);

    Assert.That(result.IsEmpty, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Stream contains only ftyp + moov with no moof/mdat following
  ///
  /// ACTION:
  /// ReadInitSegment
  ///
  /// EXPECTED RESULT:
  /// Returns empty (no moof/mdat boundary found)
  /// </summary>
  [Test]
  public async Task ReadInitSegment_NoFragments_ReturnsEmpty()
  {
    var ms = new MemoryStream();
    WriteBox(ms, "ftyp", 8);
    WriteBox(ms, "moov", 16);
    ms.Position = 0;

    var result = await StreamSessionRunner.ReadInitSegmentAsync(ms, CancellationToken.None);

    Assert.That(result.IsEmpty, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Stream position is preserved after reading init segment
  ///
  /// ACTION:
  /// Set position to arbitrary offset, call ReadInitSegment
  ///
  /// EXPECTED RESULT:
  /// Position is restored to original value
  /// </summary>
  [Test]
  public async Task ReadInitSegment_RestoresStreamPosition()
  {
    var ms = new MemoryStream();
    WriteBox(ms, "ftyp", 8);
    WriteBox(ms, "moov", 16);
    WriteBox(ms, "moof", 8);
    ms.Position = 10;

    await StreamSessionRunner.ReadInitSegmentAsync(ms, CancellationToken.None);

    Assert.That(ms.Position, Is.EqualTo(10));
  }

  /// <summary>
  /// SCENARIO:
  /// Box with size=0
  ///
  /// ACTION:
  /// ReadInitSegment on stream with zero-size box
  ///
  /// EXPECTED RESULT:
  /// Returns empty (size=0 breaks the loop)
  /// </summary>
  [Test]
  public async Task ReadInitSegment_ZeroSizeBox_ReturnsEmpty()
  {
    var ms = new MemoryStream();
    var header = new byte[8];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, 0);
    System.Text.Encoding.ASCII.GetBytes("ftyp").CopyTo(header.AsSpan(4));
    ms.Write(header);
    ms.Position = 0;

    var result = await StreamSessionRunner.ReadInitSegmentAsync(ms, CancellationToken.None);

    Assert.That(result.IsEmpty, Is.True);
  }

  private static void WriteBox(MemoryStream ms, string type, int totalSize)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(totalSize, 8);
    var header = new byte[8];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, (uint)totalSize);
    System.Text.Encoding.ASCII.GetBytes(type).CopyTo(header.AsSpan(4));
    ms.Write(header);
    ms.Write(new byte[totalSize - 8]);
  }
}

[TestFixture]
public class StreamSessionRunnerLiveTests
{
  /// <summary>
  /// SCENARIO:
  /// RunLiveAsync called with no pipeline registered for the camera
  ///
  /// ACTION:
  /// Run live with empty registry
  ///
  /// EXPECTED RESULT:
  /// Sink receives Ack, Live, then Error status
  /// </summary>
  [Test]
  public async Task RunLive_NoPipeline_SendsError()
  {
    var sink = new TestStreamSink();
    var registry = new StreamTapRegistry();

    await StreamSessionRunner.RunLiveAsync(
      Guid.NewGuid(), "main", sink, registry,
      NullLogger.Instance, CancellationToken.None);

    Assert.That(sink.Statuses, Is.EqualTo(new[]
    {
      StreamStatus.Ack, StreamStatus.Live, StreamStatus.Error
    }));
  }

}

[TestFixture]
public class StreamSessionRunnerFetchTests
{
  /// <summary>
  /// SCENARIO:
  /// RunFetchAsync with no storage provider
  ///
  /// ACTION:
  /// Run fetch with plugin host that has no storage providers
  ///
  /// EXPECTED RESULT:
  /// Sink receives Ack, Recording, then Error
  /// </summary>
  [Test]
  public async Task RunFetch_NoStorage_SendsError()
  {
    var sink = new TestStreamSink();
    var plugins = new SessionTestPluginHost(
      dataProvider: new StubDataProvider(),
      storageProviders: []);

    await StreamSessionRunner.RunFetchAsync(
      Guid.NewGuid(), "main", 1000, 2000, sink, new StreamTapRegistry(), plugins,
      NullLogger.Instance, CancellationToken.None);

    Assert.That(sink.Statuses, Is.EqualTo(new[]
    {
      StreamStatus.Ack, StreamStatus.Recording, StreamStatus.Error
    }));
  }

  /// <summary>
  /// SCENARIO:
  /// RunFetchAsync with stream lookup that fails
  ///
  /// ACTION:
  /// Run fetch with stream repository that returns error
  ///
  /// EXPECTED RESULT:
  /// Sink receives Ack, Recording, then Error
  /// </summary>
  [Test]
  public async Task RunFetch_StreamLookupFails_SendsError()
  {
    var sink = new TestStreamSink();
    var plugins = new SessionTestPluginHost(
      dataProvider: new StubDataProvider(
        streams: new StubStreamRepository(
          error: Error.Create(0, 0, Result.InternalError, "db error"))));

    await StreamSessionRunner.RunFetchAsync(
      Guid.NewGuid(), "main", 1000, 2000, sink, new StreamTapRegistry(), plugins,
      NullLogger.Instance, CancellationToken.None);

    Assert.That(sink.Statuses, Is.EqualTo(new[]
    {
      StreamStatus.Ack, StreamStatus.Recording, StreamStatus.Error
    }));
  }

  /// <summary>
  /// SCENARIO:
  /// RunFetchAsync with no matching stream profile
  ///
  /// ACTION:
  /// Run fetch requesting "nonexistent" when only "main" exists
  ///
  /// EXPECTED RESULT:
  /// Sink receives Ack, Recording, then Error
  /// </summary>
  [Test]
  public async Task RunFetch_NoMatchingProfile_SendsError()
  {
    var sink = new TestStreamSink();
    var cameraId = Guid.NewGuid();
    var plugins = new SessionTestPluginHost(
      dataProvider: new StubDataProvider(
        streams: new StubStreamRepository(streams: [
          new CameraStream
          {
            Id = Guid.NewGuid(), CameraId = cameraId, Profile = "main",
            FormatId = "fmp4", Uri = "rtsp://test"
          }
        ])));

    await StreamSessionRunner.RunFetchAsync(
      cameraId, "nonexistent", 1000, 2000, sink, new StreamTapRegistry(), plugins,
      NullLogger.Instance, CancellationToken.None);

    Assert.That(sink.Statuses, Is.EqualTo(new[]
    {
      StreamStatus.Ack, StreamStatus.Recording, StreamStatus.Error
    }));
  }

  /// <summary>
  /// SCENARIO:
  /// RunFetchAsync with no playback point found for the requested timestamp
  ///
  /// ACTION:
  /// Fetch with segment repo returning NotFound
  ///
  /// EXPECTED RESULT:
  /// Sends FetchComplete (no recordings at requested time, client decides next action)
  /// </summary>
  [Test]
  public async Task RunFetch_NoPlaybackPoint_SendsFetchComplete()
  {
    var sink = new TestStreamSink();
    var cameraId = Guid.NewGuid();
    var streamId = Guid.NewGuid();
    var plugins = new SessionTestPluginHost(
      dataProvider: new StubDataProvider(
        streams: new StubStreamRepository(streams: [
          new CameraStream
          {
            Id = streamId, CameraId = cameraId, Profile = "main",
            FormatId = "fmp4", Uri = "rtsp://test"
          }
        ]),
        segments: new StubSegmentRepository(
          playbackError: Error.Create(0, 0, Result.NotFound, "not found"))));

    await StreamSessionRunner.RunFetchAsync(
      cameraId, "main", 1000, 2000, sink, new StreamTapRegistry(), plugins,
      NullLogger.Instance, CancellationToken.None);

    Assert.That(sink.Statuses, Is.EqualTo(new[]
    {
      StreamStatus.Ack, StreamStatus.Recording, StreamStatus.FetchComplete
    }));
  }

  /// <summary>
  /// SCENARIO:
  /// RunFetchAsync with no matching format plugin
  ///
  /// ACTION:
  /// Fetch with valid segment but no format plugin matches the stream's formatId
  ///
  /// EXPECTED RESULT:
  /// Sends error
  /// </summary>
  [Test]
  public async Task RunFetch_NoMatchingFormat_SendsError()
  {
    var sink = new TestStreamSink();
    var cameraId = Guid.NewGuid();
    var streamId = Guid.NewGuid();
    var segmentId = Guid.NewGuid();
    var plugins = new SessionTestPluginHost(
      dataProvider: new StubDataProvider(
        streams: new StubStreamRepository(streams: [
          new CameraStream
          {
            Id = streamId, CameraId = cameraId, Profile = "main",
            FormatId = "fmp4", Uri = "rtsp://test"
          }
        ]),
        segments: new StubSegmentRepository(
          playbackPoint: new PlaybackPoint
          {
            SegmentId = segmentId, SegmentRef = "test.mp4",
            KeyframeTimestamp = 1000, ByteOffset = 0
          },
          segment: new Segment
          {
            Id = segmentId, StreamId = streamId,
            StartTime = 1000, EndTime = 2000,
            SegmentRef = "test.mp4", SizeBytes = 1024, KeyframeCount = 1
          })),
      streamFormats: []);

    await StreamSessionRunner.RunFetchAsync(
      cameraId, "main", 1000, 2000, sink, new StreamTapRegistry(), plugins,
      NullLogger.Instance, CancellationToken.None);

    Assert.That(sink.Statuses, Has.Member(StreamStatus.Error));
  }
}
