using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class EventTests
{
  private HttpClient _client = null!;
  private IDataProvider _data = null!;

  private Guid _cameraId;
  private Guid _eventId;

  [OneTimeSetUp]
  public async Task Setup()
  {
    _client = ApiTestFixture.Client;
    _data = ApiTestFixture.App.Services.GetRequiredService<IDataProvider>();

    _cameraId = Guid.NewGuid();
    _eventId = Guid.NewGuid();

    await _data.Cameras.CreateAsync(new Camera
    {
      Id = _cameraId,
      Name = "Event Test Cam",
      Address = $"192.168.98.{Random.Shared.Next(1, 254)}",
      ProviderId = "test",
      CreatedAt = 1000000UL,
      UpdatedAt = 1000000UL
    });

    await _data.Events.CreateAsync(new CameraEvent
    {
      Id = _eventId,
      CameraId = _cameraId,
      Type = "motion",
      StartTime = 5000000UL,
      EndTime = 6000000UL,
      Metadata = new Dictionary<string, string> { ["zone"] = "front" }
    });

    await _data.Events.CreateAsync(new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = _cameraId,
      Type = "tamper",
      StartTime = 7000000UL
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Two events exist for a camera within a time range
  ///
  /// ACTION:
  /// GET /api/v1/events?from=0&amp;to=99999999
  ///
  /// EXPECTED RESULT:
  /// 200 with array containing at least 2 events
  /// </summary>
  [Test]
  public async Task QueryEvents_ReturnsMatchingEvents()
  {
    var response = await _client.GetAsync("/api/v1/events?from=0&to=99999999");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var events = (await ApiTestFixture.Envelope<EventDto[]>(response)).Body!;
    Assert.That(events, Has.Length.GreaterThanOrEqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// Events exist for a specific camera
  ///
  /// ACTION:
  /// GET /api/v1/events?cameraId={id}&amp;from=0&amp;to=99999999
  ///
  /// EXPECTED RESULT:
  /// 200 with only events for that camera
  /// </summary>
  [Test]
  public async Task QueryEvents_FilterByCameraId()
  {
    var response = await _client.GetAsync(
      $"/api/v1/events?cameraId={_cameraId}&from=0&to=99999999");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var events = (await ApiTestFixture.Envelope<EventDto[]>(response)).Body!;
    Assert.That(events, Has.Length.EqualTo(2));
    Assert.That(events, Has.All.Property(nameof(EventDto.CameraId)).EqualTo(_cameraId));
  }

  /// <summary>
  /// SCENARIO:
  /// Events of different types exist
  ///
  /// ACTION:
  /// GET /api/v1/events?type=motion&amp;from=0&amp;to=99999999
  ///
  /// EXPECTED RESULT:
  /// 200 with only motion-type events
  /// </summary>
  [Test]
  public async Task QueryEvents_FilterByType()
  {
    var response = await _client.GetAsync(
      $"/api/v1/events?cameraId={_cameraId}&type=motion&from=0&to=99999999");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var events = (await ApiTestFixture.Envelope<EventDto[]>(response)).Body!;
    Assert.That(events, Has.Length.EqualTo(1));
    Assert.That(events[0].Type, Is.EqualTo("motion"));
  }

  /// <summary>
  /// SCENARIO:
  /// Multiple events exist
  ///
  /// ACTION:
  /// GET /api/v1/events?from=0&amp;to=99999999&amp;limit=1
  ///
  /// EXPECTED RESULT:
  /// 200 with exactly 1 event (limit is respected)
  /// </summary>
  [Test]
  public async Task QueryEvents_LimitRespected()
  {
    var response = await _client.GetAsync(
      $"/api/v1/events?cameraId={_cameraId}&from=0&to=99999999&limit=1");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var events = (await ApiTestFixture.Envelope<EventDto[]>(response)).Body!;
    Assert.That(events, Has.Length.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A known event exists in the database
  ///
  /// ACTION:
  /// GET /api/v1/events/{eventId}
  ///
  /// EXPECTED RESULT:
  /// 200 with the event's id, cameraId, type, startTime, endTime, and metadata
  /// </summary>
  [Test]
  public async Task GetEvent_ReturnsCorrectFields()
  {
    var response = await _client.GetAsync($"/api/v1/events/{_eventId}");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var body = (await ApiTestFixture.Envelope<EventDto>(response)).Body!;
    Assert.That(body.Id, Is.EqualTo(_eventId));
    Assert.That(body.CameraId, Is.EqualTo(_cameraId));
    Assert.That(body.Type, Is.EqualTo("motion"));
    Assert.That(body.StartTime, Is.EqualTo(5000000UL));
    Assert.That(body.EndTime, Is.EqualTo(6000000UL));
    Assert.That(body.Metadata!["zone"], Is.EqualTo("front"));
  }

  /// <summary>
  /// SCENARIO:
  /// No event with the given ID exists
  ///
  /// ACTION:
  /// GET /api/v1/events/{random guid}
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result
  /// </summary>
  [Test]
  public async Task GetEvent_NotFound()
  {
    var response = await _client.GetAsync($"/api/v1/events/{Guid.NewGuid()}");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// Events exist but query time range excludes them
  ///
  /// ACTION:
  /// GET /api/v1/events?from=90000000&amp;to=99999999
  ///
  /// EXPECTED RESULT:
  /// 200 with empty array
  /// </summary>
  [Test]
  public async Task QueryEvents_OutOfRangeReturnsEmpty()
  {
    var response = await _client.GetAsync(
      $"/api/v1/events?cameraId={_cameraId}&from=90000000&to=99999999");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var events = (await ApiTestFixture.Envelope<EventDto[]>(response)).Body!;
    Assert.That(events, Is.Empty);
  }
}
