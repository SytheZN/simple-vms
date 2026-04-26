using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Server.Plugins;
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
  /// Camera list is queried
  ///
  /// ACTION:
  /// GET /api/v1/cameras
  ///
  /// EXPECTED RESULT:
  /// 200 with a valid array; each item has required fields populated
  /// </summary>
  [Test]
  public async Task ListCameras_ReturnsValidArray()
  {
    var response = await _client.GetAsync("/api/v1/cameras");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<CameraListItem[]>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));

    foreach (var cam in envelope.Body!)
    {
      Assert.That(cam.Id, Is.Not.EqualTo(Guid.Empty));
      Assert.That(cam.Name, Is.Not.Null.And.Not.Empty);
      Assert.That(cam.Address, Is.Not.Null.And.Not.Empty);
      Assert.That(cam.Status, Is.Not.Null.And.Not.Empty);
      Assert.That(cam.ProviderId, Is.Not.Null.And.Not.Empty);
    }
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
  /// Error because the provider fails to connect to the camera during refresh
  /// </summary>
  [Test]
  [CancelAfter(5000)]
  public async Task CreateCamera_UnreachableReturnsError()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/cameras",
      new { address = "http://127.0.0.1:1/onvif/device_service" });
    Assert.That(response.IsSuccessStatusCode, Is.False);

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.Not.EqualTo(Result.Success));
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

  /// <summary>
  /// SCENARIO:
  /// Camera provider is available but camera is unreachable
  ///
  /// ACTION:
  /// POST /api/v1/cameras/probe with an unreachable address
  ///
  /// EXPECTED RESULT:
  /// Error because the provider fails to connect
  /// </summary>
  [Test]
  [CancelAfter(5000)]
  public async Task ProbeCamera_UnreachableReturnsError()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/cameras/probe",
      new { address = "http://127.0.0.1:1/onvif/device_service" });
    Assert.That(response.IsSuccessStatusCode, Is.False);

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.Not.EqualTo(Result.Success));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is created directly via the data provider and then updated via the API
  ///
  /// ACTION:
  /// Insert a camera, PUT /api/v1/cameras/{id} for the name change, and
  /// PUT /api/v1/cameras/{id}/config with core retention fields
  ///
  /// EXPECTED RESULT:
  /// 200 on both calls; GET returns the new name; the data store reflects the new retention
  /// </summary>
  [Test]
  public async Task UpdateCamera_ChangesNameAndRetention()
  {
    var pluginHost = ApiTestFixture.App.Services.GetRequiredService<IPluginHost>();
    var camera = new Shared.Models.Camera
    {
      Id = Guid.NewGuid(),
      Name = "Test Update",
      Address = $"http://192.0.2.{Random.Shared.Next(1, 254)}/onvif/device_service",
      ProviderId = "onvif",
      CreatedAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000,
      UpdatedAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
    };
    await pluginHost.DataProvider.Cameras.CreateAsync(camera, CancellationToken.None);

    var renameResponse = await _client.PutAsJsonAsync($"/api/v1/cameras/{camera.Id}", new
    {
      name = "Renamed Camera"
    });
    Assert.That(renameResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var configResponse = await _client.PutAsJsonAsync($"/api/v1/cameras/{camera.Id}/config", new
    {
      camera = new Dictionary<string, object>
      {
        ["core"] = new Dictionary<string, string>
        {
          ["retentionMode"] = "days",
          ["retentionValue"] = "14"
        }
      }
    });
    Assert.That(configResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var getResponse = await _client.GetAsync($"/api/v1/cameras/{camera.Id}");
    var body = (await ApiTestFixture.Envelope<CameraListItem>(getResponse)).Body!;
    Assert.That(body.Name, Is.EqualTo("Renamed Camera"));

    var fetched = (await pluginHost.DataProvider.Cameras.GetByIdAsync(camera.Id, CancellationToken.None)).AsT0;
    Assert.That(fetched.RetentionMode, Is.EqualTo(Shared.Models.RetentionMode.Days));
    Assert.That(fetched.RetentionValue, Is.EqualTo(14));

    await _client.DeleteAsync($"/api/v1/cameras/{camera.Id}");
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is created directly via the data provider
  ///
  /// ACTION:
  /// DELETE /api/v1/cameras/{id}
  ///
  /// EXPECTED RESULT:
  /// 200, and GET returns 404
  /// </summary>
  [Test]
  public async Task DeleteCamera_RemovesCamera()
  {
    var pluginHost = ApiTestFixture.App.Services.GetRequiredService<IPluginHost>();
    var camera = new Shared.Models.Camera
    {
      Id = Guid.NewGuid(),
      Name = "Test Delete",
      Address = $"http://192.0.2.{Random.Shared.Next(1, 254)}/onvif/device_service",
      ProviderId = "onvif",
      CreatedAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000,
      UpdatedAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
    };
    await pluginHost.DataProvider.Cameras.CreateAsync(camera, CancellationToken.None);

    var deleteResponse = await _client.DeleteAsync($"/api/v1/cameras/{camera.Id}");
    Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var getResponse = await _client.GetAsync($"/api/v1/cameras/{camera.Id}");
    Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera is created with a stream, then stream recording is toggled off via the unified config endpoint
  ///
  /// ACTION:
  /// Create camera + stream, PUT /api/v1/cameras/{id}/config with streams.main.core.recordingEnabled="false"
  ///
  /// EXPECTED RESULT:
  /// The stream's RecordingEnabled flag is false in the data store
  /// </summary>
  [Test]
  public async Task UpdateCameraConfig_TogglesStreamRecording()
  {
    var pluginHost = ApiTestFixture.App.Services.GetRequiredService<IPluginHost>();
    var camera = new Shared.Models.Camera
    {
      Id = Guid.NewGuid(),
      Name = "Test Stream Toggle",
      Address = $"http://192.0.2.{Random.Shared.Next(1, 254)}/onvif/device_service",
      ProviderId = "onvif",
      CreatedAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000,
      UpdatedAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
    };
    await pluginHost.DataProvider.Cameras.CreateAsync(camera, CancellationToken.None);

    var stream = new Shared.Models.CameraStream
    {
      Id = Guid.NewGuid(),
      CameraId = camera.Id,
      Profile = "main",
      FormatId = "fmp4",
      Codec = "h264",
      Resolution = "1920x1080",
      Fps = 30,
      Uri = "rtsp://192.0.2.1/stream1",
      RecordingEnabled = true
    };
    await pluginHost.DataProvider.Streams.UpsertAsync(stream, CancellationToken.None);

    var updateResponse = await _client.PutAsJsonAsync($"/api/v1/cameras/{camera.Id}/config", new
    {
      streams = new Dictionary<string, object>
      {
        ["main"] = new Dictionary<string, object>
        {
          ["core"] = new Dictionary<string, string> { ["recordingEnabled"] = "false" }
        }
      }
    });
    Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var fetched = (await pluginHost.DataProvider.Streams.GetByIdAsync(stream.Id, CancellationToken.None)).AsT0;
    Assert.That(fetched.RecordingEnabled, Is.False);

    await _client.DeleteAsync($"/api/v1/cameras/{camera.Id}");
  }
}
