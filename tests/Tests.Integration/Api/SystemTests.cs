using System.Net;
using System.Net.Http.Json;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class SystemTests
{
  private HttpClient _client = null!;

  [OneTimeSetUp]
  public void Setup() => _client = ApiTestFixture.Client;

  /// <summary>
  /// SCENARIO:
  /// Server is running
  ///
  /// ACTION:
  /// GET /api/v1/system/health
  ///
  /// EXPECTED RESULT:
  /// 200 with status, uptime >= 0, camera counts, storage object, and version string
  /// </summary>
  [Test]
  public async Task Health_ReturnsAllFields()
  {
    var response = await _client.GetAsync("/api/v1/system/health");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<HealthResponse>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.DebugTag, Is.Not.EqualTo(DebugTag.None));

    var body = envelope.Body!;
    Assert.That(body.Status, Is.AnyOf("healthy", "degraded", "unhealthy"));
    Assert.That(body.Uptime, Is.GreaterThanOrEqualTo(0));
    Assert.That(body.Version, Is.Not.Null);
    Assert.That(body.Cameras.Total, Is.GreaterThanOrEqualTo(0));
    Assert.That(body.Cameras.Online, Is.GreaterThanOrEqualTo(0));
    Assert.That(body.Cameras.Offline, Is.GreaterThanOrEqualTo(0));
    Assert.That(body.Cameras.Error, Is.GreaterThanOrEqualTo(0));
    Assert.That(body.Storage.Stores, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// No storage providers are registered
  ///
  /// ACTION:
  /// GET /api/v1/system/storage
  ///
  /// EXPECTED RESULT:
  /// 200 with a body containing a stores array (empty since no providers)
  /// </summary>
  [Test]
  public async Task Storage_ReturnsStoresArray()
  {
    var response = await _client.GetAsync("/api/v1/system/storage");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var body = (await ApiTestFixture.Envelope<StorageResponse>(response)).Body!;
    Assert.That(body.Stores, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// No settings have been configured
  ///
  /// ACTION:
  /// GET /api/v1/system/settings
  ///
  /// EXPECTED RESULT:
  /// 200 with a ServerSettings body
  /// </summary>
  [Test]
  public async Task Settings_GetReturnsObject()
  {
    var response = await _client.GetAsync("/api/v1/system/settings");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<ServerSettings>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.Body, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// No settings have been configured
  ///
  /// ACTION:
  /// PUT /api/v1/system/settings with serverName and segmentDuration, then GET
  ///
  /// EXPECTED RESULT:
  /// PUT returns 200. GET returns the updated values.
  /// </summary>
  [Test]
  public async Task Settings_UpdateAndVerify()
  {
    var updateResponse = await _client.PutAsJsonAsync("/api/v1/system/settings",
      new { serverName = "Integration Test", segmentDuration = 120 });
    Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var body = (await ApiTestFixture.Envelope<ServerSettings>(
      await _client.GetAsync("/api/v1/system/settings"))).Body!;
    Assert.That(body.ServerName, Is.EqualTo("Integration Test"));
    Assert.That(body.SegmentDuration, Is.EqualTo(120));
  }

  /// <summary>
  /// SCENARIO:
  /// Settings have been previously set
  ///
  /// ACTION:
  /// PUT /api/v1/system/settings with only externalEndpoint
  ///
  /// EXPECTED RESULT:
  /// Only the specified field is updated; other fields remain unchanged
  /// </summary>
  [Test]
  public async Task Settings_PartialUpdate()
  {
    await _client.PutAsJsonAsync("/api/v1/system/settings",
      new { serverName = "Partial Test" });

    await _client.PutAsJsonAsync("/api/v1/system/settings",
      new { externalEndpoint = "myhome.ddns.net" });

    var body = (await ApiTestFixture.Envelope<ServerSettings>(
      await _client.GetAsync("/api/v1/system/settings"))).Body!;
    Assert.That(body.ServerName, Is.EqualTo("Partial Test"));
    Assert.That(body.ExternalEndpoint, Is.EqualTo("myhome.ddns.net"));
  }

  /// <summary>
  /// SCENARIO:
  /// Settings include discoverySubnets
  ///
  /// ACTION:
  /// PUT /api/v1/system/settings with discoverySubnets array, then GET
  ///
  /// EXPECTED RESULT:
  /// GET returns the subnets as an array
  /// </summary>
  [Test]
  public async Task Settings_DiscoverySubnets()
  {
    await _client.PutAsJsonAsync("/api/v1/system/settings",
      new { discoverySubnets = new[] { "10.0.0.0/24", "172.16.0.0/16" } });

    var body = (await ApiTestFixture.Envelope<ServerSettings>(
      await _client.GetAsync("/api/v1/system/settings"))).Body!;
    Assert.That(body.DiscoverySubnets, Has.Length.EqualTo(2));
    Assert.That(body.DiscoverySubnets![0], Is.EqualTo("10.0.0.0/24"));
    Assert.That(body.DiscoverySubnets[1], Is.EqualTo("172.16.0.0/16"));
  }
}
