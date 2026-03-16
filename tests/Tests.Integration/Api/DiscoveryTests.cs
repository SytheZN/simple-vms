using System.Net;
using System.Net.Http.Json;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class DiscoveryTests
{
  private HttpClient _client = null!;

  [OneTimeSetUp]
  public void Setup() => _client = ApiTestFixture.Client;

  /// <summary>
  /// SCENARIO:
  /// No camera providers are registered
  ///
  /// ACTION:
  /// POST /api/v1/discovery with no body filters
  ///
  /// EXPECTED RESULT:
  /// 200 with an empty array (no providers to discover cameras)
  /// </summary>
  [Test]
  public async Task Discover_NoProvidersReturnsEmptyArray()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/discovery", new { });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<DiscoveredCameraDto[]>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.Body, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// No camera providers are registered
  ///
  /// ACTION:
  /// POST /api/v1/discovery with subnet and credential filters
  ///
  /// EXPECTED RESULT:
  /// 200 with an empty array (filters are accepted but no providers run)
  /// </summary>
  [Test]
  public async Task Discover_WithFiltersReturnsEmptyArray()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/discovery",
      new
      {
        subnets = new[] { "192.168.1.0/24" },
        credentials = new { username = "admin", password = "admin" }
      });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<DiscoveredCameraDto[]>(response);
    Assert.That(envelope.Body, Is.Empty);
  }
}
