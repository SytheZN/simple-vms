using System.Net;
using System.Net.Http.Json;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class ClientTests
{
  private HttpClient _client = null!;

  [OneTimeSetUp]
  public void Setup() => _client = ApiTestFixture.Client;

  /// <summary>
  /// SCENARIO:
  /// Server has enrolled clients from other tests
  ///
  /// ACTION:
  /// GET /api/v1/clients
  ///
  /// EXPECTED RESULT:
  /// 200 with an array of ClientListItem
  /// </summary>
  [Test]
  public async Task ListClients_ReturnsArray()
  {
    var response = await _client.GetAsync("/api/v1/clients");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<ClientListItem[]>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.Body, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// A client has been enrolled
  ///
  /// ACTION:
  /// GET /api/v1/clients/{clientId}
  ///
  /// EXPECTED RESULT:
  /// 200 with the client's id, name, enrolledAt, lastSeenAt, and connected fields
  /// </summary>
  [Test]
  public async Task GetClient_ReturnsCorrectFields()
  {
    var clientId = await ApiTestFixture.EnrollClientAsync();

    var response = await _client.GetAsync($"/api/v1/clients/{clientId}");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var body = (await ApiTestFixture.Envelope<ClientListItem>(response)).Body!;
    Assert.That(body.Id.ToString(), Is.EqualTo(clientId));
    Assert.That(body.Name, Is.Not.Null.And.Not.Empty);
    Assert.That(body.EnrolledAt, Is.GreaterThan(0UL));
    Assert.That(body.Connected, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// No client with the given ID exists
  ///
  /// ACTION:
  /// GET /api/v1/clients/{random guid}
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result and a message
  /// </summary>
  [Test]
  public async Task GetClient_NotFound()
  {
    var response = await _client.GetAsync($"/api/v1/clients/{Guid.NewGuid()}");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
    Assert.That(envelope.Message, Is.Not.Null.And.Not.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A client has been enrolled
  ///
  /// ACTION:
  /// PUT /api/v1/clients/{clientId} with a new name, then GET to verify
  ///
  /// EXPECTED RESULT:
  /// PUT returns 200. GET returns the updated name.
  /// </summary>
  [Test]
  public async Task UpdateClient_ChangesName()
  {
    var clientId = await ApiTestFixture.EnrollClientAsync();

    var updateResponse = await _client.PutAsJsonAsync(
      $"/api/v1/clients/{clientId}", new { name = "Living Room Tablet" });
    Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var body = (await ApiTestFixture.Envelope<ClientListItem>(
      await _client.GetAsync($"/api/v1/clients/{clientId}"))).Body!;
    Assert.That(body.Name, Is.EqualTo("Living Room Tablet"));
  }

  /// <summary>
  /// SCENARIO:
  /// No client with the given ID exists
  ///
  /// ACTION:
  /// PUT /api/v1/clients/{random guid}
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result
  /// </summary>
  [Test]
  public async Task UpdateClient_NotFound()
  {
    var response = await _client.PutAsJsonAsync(
      $"/api/v1/clients/{Guid.NewGuid()}", new { name = "X" });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// A client has been enrolled
  ///
  /// ACTION:
  /// DELETE /api/v1/clients/{clientId}
  ///
  /// EXPECTED RESULT:
  /// 200, and subsequent GET returns 404 (revoked clients are excluded)
  /// </summary>
  [Test]
  public async Task RevokeClient_ExcludesFromApi()
  {
    var clientId = await ApiTestFixture.EnrollClientAsync();

    var revokeResponse = await _client.DeleteAsync($"/api/v1/clients/{clientId}");
    Assert.That(revokeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope(revokeResponse);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));

    var getResponse = await _client.GetAsync($"/api/v1/clients/{clientId}");
    Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// No client with the given ID exists
  ///
  /// ACTION:
  /// DELETE /api/v1/clients/{random guid}
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result
  /// </summary>
  [Test]
  public async Task RevokeClient_NotFound()
  {
    var response = await _client.DeleteAsync($"/api/v1/clients/{Guid.NewGuid()}");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
  }
}
