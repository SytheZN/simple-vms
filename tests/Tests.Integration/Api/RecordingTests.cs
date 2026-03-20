using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Server.Plugins;
using Shared.Models;

using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class RecordingTests
{
  private HttpClient _client = null!;
  private IDataProvider _data = null!;

  private Guid _cameraId;
  private Guid _streamId;

  [OneTimeSetUp]
  public async Task Setup()
  {
    _client = ApiTestFixture.Client;
    _data = ApiTestFixture.App.Services.GetRequiredService<IPluginHost>().DataProvider;

    _cameraId = Guid.NewGuid();
    _streamId = Guid.NewGuid();

    await _data.Cameras.CreateAsync(new Camera
    {
      Id = _cameraId,
      Name = "Recording Test Cam",
      Address = $"192.168.99.{Random.Shared.Next(1, 254)}",
      ProviderId = "test",
      CreatedAt = 1000000UL,
      UpdatedAt = 1000000UL
    });

    await _data.Streams.UpsertAsync(new CameraStream
    {
      Id = _streamId,
      CameraId = _cameraId,
      Profile = "main",
      FormatId = "fmp4",
      Codec = "h264",
      Resolution = "1920x1080",
      Fps = 30,
      Uri = "rtsp://fake",
      RecordingEnabled = true
    });

    await _data.Segments.CreateAsync(new Segment
    {
      Id = Guid.NewGuid(),
      StreamId = _streamId,
      StartTime = 1000000UL,
      EndTime = 2000000UL,
      SegmentRef = "/fake/segment1.mp4",
      SizeBytes = 1024000,
      KeyframeCount = 10
    });

    await _data.Segments.CreateAsync(new Segment
    {
      Id = Guid.NewGuid(),
      StreamId = _streamId,
      StartTime = 2000000UL,
      EndTime = 3000000UL,
      SegmentRef = "/fake/segment2.mp4",
      SizeBytes = 2048000,
      KeyframeCount = 12
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A camera has two recorded segments
  ///
  /// ACTION:
  /// GET /api/v1/recordings/{cameraId}?from=0&amp;to=9999999&amp;profile=main
  ///
  /// EXPECTED RESULT:
  /// 200 with array of 2 segments, each having id, startTime, endTime, profile, sizeBytes
  /// </summary>
  [Test]
  public async Task GetSegments_ReturnsBothSegments()
  {
    var response = await _client.GetAsync(
      $"/api/v1/recordings/{_cameraId}?from=0&to=9999999&profile=main");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var segments = (await ApiTestFixture.Envelope<RecordingSegmentDto[]>(response)).Body!;
    Assert.That(segments, Has.Length.EqualTo(2));

    Assert.That(segments[0].StartTime, Is.EqualTo(1000000UL));
    Assert.That(segments[0].EndTime, Is.EqualTo(2000000UL));
    Assert.That(segments[0].Profile, Is.EqualTo("main"));
    Assert.That(segments[0].SizeBytes, Is.EqualTo(1024000L));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera has two contiguous recorded segments
  ///
  /// ACTION:
  /// GET /api/v1/recordings/{cameraId}/timeline?from=0&amp;to=9999999&amp;profile=main
  ///
  /// EXPECTED RESULT:
  /// 200 with spans array (contiguous segments merged into one span) and events array
  /// </summary>
  [Test]
  public async Task GetTimeline_MergesContiguousSpans()
  {
    var response = await _client.GetAsync(
      $"/api/v1/recordings/{_cameraId}/timeline?from=0&to=9999999&profile=main");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var timeline = (await ApiTestFixture.Envelope<TimelineResponse>(response)).Body!;
    Assert.That(timeline.Spans, Has.Count.EqualTo(1));
    Assert.That(timeline.Spans[0].StartTime, Is.EqualTo(1000000UL));
    Assert.That(timeline.Spans[0].EndTime, Is.EqualTo(3000000UL));
    Assert.That(timeline.Events, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera exists but has no stream with the requested profile
  ///
  /// ACTION:
  /// GET /api/v1/recordings/{cameraId}?from=0&amp;to=9999999&amp;profile=sub
  ///
  /// EXPECTED RESULT:
  /// 404 because no stream with profile "sub" exists for this camera
  /// </summary>
  [Test]
  public async Task GetSegments_NoMatchingProfileReturnsNotFound()
  {
    var response = await _client.GetAsync(
      $"/api/v1/recordings/{_cameraId}?from=0&to=9999999&profile=sub");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
    Assert.That(envelope.Message, Does.Contain("sub"));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera has segments but query range excludes them
  ///
  /// ACTION:
  /// GET /api/v1/recordings/{cameraId}?from=9000000&amp;to=9999999&amp;profile=main
  ///
  /// EXPECTED RESULT:
  /// 200 with empty array
  /// </summary>
  [Test]
  public async Task GetSegments_OutOfRangeReturnsEmpty()
  {
    var response = await _client.GetAsync(
      $"/api/v1/recordings/{_cameraId}?from=9000000&to=9999999&profile=main");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var segments = (await ApiTestFixture.Envelope<RecordingSegmentDto[]>(response)).Body!;
    Assert.That(segments, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Querying recordings for a camera that does not exist
  ///
  /// ACTION:
  /// GET /api/v1/recordings/{random guid}?from=0&amp;to=9999999
  ///
  /// EXPECTED RESULT:
  /// 404 because the camera has no streams
  /// </summary>
  [Test]
  public async Task GetSegments_NonexistentCameraReturnsNotFound()
  {
    var response = await _client.GetAsync(
      $"/api/v1/recordings/{Guid.NewGuid()}?from=0&to=9999999");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// Querying timeline defaults to "main" profile when not specified
  ///
  /// ACTION:
  /// GET /api/v1/recordings/{cameraId}/timeline?from=0&amp;to=9999999 (no profile param)
  ///
  /// EXPECTED RESULT:
  /// 200 with timeline data for the main profile
  /// </summary>
  [Test]
  public async Task GetTimeline_DefaultsToMainProfile()
  {
    var response = await _client.GetAsync(
      $"/api/v1/recordings/{_cameraId}/timeline?from=0&to=9999999");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var timeline = (await ApiTestFixture.Envelope<TimelineResponse>(response)).Body!;
    Assert.That(timeline.Spans, Has.Count.GreaterThan(0));
  }
}
