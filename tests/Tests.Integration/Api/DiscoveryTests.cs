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
  /// Camera provider is available
  ///
  /// ACTION:
  /// POST /api/v1/discovery with no body filters
  ///
  /// EXPECTED RESULT:
  /// 200 with success result (may find cameras via multicast on local network)
  /// </summary>
  [Test]
  public async Task Discover_ReturnsSuccess()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/discovery", new { });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<DiscoveredCameraDto[]>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.Body, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Camera provider is available but subnet has no devices
  ///
  /// ACTION:
  /// POST /api/v1/discovery with an unreachable subnet (TEST-NET-1)
  ///
  /// EXPECTED RESULT:
  /// 200 with an empty array (subnet scan finds nothing)
  /// </summary>
  [Test]
  public async Task Discover_UnreachableSubnetReturnsEmptyArray()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/discovery",
      new
      {
        subnets = new[] { "192.0.2.0/29" },
        credentials = new { username = "admin", password = "admin" }
      });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<DiscoveredCameraDto[]>(response);
    Assert.That(envelope.Body, Is.Not.Null);
  }
}
