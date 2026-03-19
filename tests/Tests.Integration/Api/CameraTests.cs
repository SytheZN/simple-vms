using System.Net;
using System.Net.Http.Json;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class CameraTests
{
  private HttpClient _client = null!;

  [OneTimeSetUp]
  public void Setup() => _client = ApiTestFixture.Client;

  /// <summary>
  /// SCENARIO:
  /// No cameras registered
  ///
  /// ACTION:
  /// GET /api/v1/cameras
  ///
  /// EXPECTED RESULT:
  /// 200 with an empty array body
  /// </summary>
  [Test]
  public async Task ListCameras_EmptyReturnsEmptyArray()
  {
    var response = await _client.GetAsync("/api/v1/cameras");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<CameraListItem[]>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.Body, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// No camera with the given ID exists
  ///
  /// ACTION:
  /// GET /api/v1/cameras/{random guid}
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result, a message, and no body
  /// </summary>
  [Test]
  public async Task GetCamera_NotFound()
  {
    var response = await _client.GetAsync($"/api/v1/cameras/{Guid.NewGuid()}");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
    Assert.That(envelope.Message, Is.Not.Null.And.Not.Empty);
    Assert.That(envelope.Body, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// No camera with the given ID exists
  ///
  /// ACTION:
  /// PUT /api/v1/cameras/{random guid}
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result
  /// </summary>
  [Test]
  public async Task UpdateCamera_NotFound()
  {
    var response = await _client.PutAsJsonAsync(
      $"/api/v1/cameras/{Guid.NewGuid()}", new { name = "X" });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// No camera with the given ID exists
  ///
  /// ACTION:
  /// DELETE /api/v1/cameras/{random guid}
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result
  /// </summary>
  [Test]
  public async Task DeleteCamera_NotFound()
  {
    var response = await _client.DeleteAsync($"/api/v1/cameras/{Guid.NewGuid()}");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// Camera provider is available but camera is unreachable
  ///
  /// ACTION:
  /// POST /api/v1/cameras with an unreachable address
  ///
  /// EXPECTED RESULT:
  /// 500 because the provider fails to connect to the camera
  /// </summary>
  [Test]
  public async Task CreateCamera_UnreachableReturnsInternalError()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/cameras",
      new { address = "http://192.0.2.1/onvif/device_service" });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.InternalError));
    Assert.That(envelope.Message, Does.Contain("configure"));
  }

  /// <summary>
  /// SCENARIO:
  /// No camera with the given ID exists
  ///
  /// ACTION:
  /// POST /api/v1/cameras/{random guid}/restart
  ///
  /// EXPECTED RESULT:
  /// 503 because the streaming pipeline is not available
  /// </summary>
  [Test]
  public async Task RestartCamera_Unavailable()
  {
    var response = await _client.PostAsync(
      $"/api/v1/cameras/{Guid.NewGuid()}/restart", null);
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// No camera with the given ID exists
  ///
  /// ACTION:
  /// GET /api/v1/cameras/{random guid}/snapshot
  ///
  /// EXPECTED RESULT:
  /// 503 because snapshot functionality is not available
  /// </summary>
  [Test]
  public async Task GetSnapshot_Unavailable()
  {
    var response = await _client.GetAsync(
      $"/api/v1/cameras/{Guid.NewGuid()}/snapshot");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// No cameras registered, querying with status filter
  ///
  /// ACTION:
  /// GET /api/v1/cameras?status=online
  ///
  /// EXPECTED RESULT:
  /// 200 with an empty array (no cameras match the filter)
  /// </summary>
  [Test]
  public async Task ListCameras_StatusFilterReturnsEmpty()
  {
    var response = await _client.GetAsync("/api/v1/cameras?status=online");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<CameraListItem[]>(response);
    Assert.That(envelope.Body, Is.Empty);
  }
}
