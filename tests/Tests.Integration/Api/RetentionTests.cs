using System.Net;
using System.Net.Http.Json;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class RetentionTests
{
  private HttpClient _client = null!;

  [OneTimeSetUp]
  public void Setup() => _client = ApiTestFixture.Client;

  /// <summary>
  /// SCENARIO:
  /// No retention policy has been explicitly set
  ///
  /// ACTION:
  /// GET /api/v1/retention
  ///
  /// EXPECTED RESULT:
  /// 200 with a default policy containing mode and value fields
  /// </summary>
  [Test]
  public async Task GetRetention_ReturnsDefault()
  {
    var response = await _client.GetAsync("/api/v1/retention");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<RetentionPolicy>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.Body!.Mode, Is.Not.Null.And.Not.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Global retention is at its default
  ///
  /// ACTION:
  /// PUT /api/v1/retention with mode=days, value=90
  ///
  /// EXPECTED RESULT:
  /// 200 with success, and subsequent GET returns mode=days, value=90
  /// </summary>
  [Test]
  public async Task SetRetention_PersistsPolicy()
  {
    var setResponse = await _client.PutAsJsonAsync("/api/v1/retention",
      new { mode = "days", value = 90L });
    Assert.That(setResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var body = (await ApiTestFixture.Envelope<RetentionPolicy>(
      await _client.GetAsync("/api/v1/retention"))).Body!;
    Assert.That(body.Mode, Is.EqualTo("days"));
    Assert.That(body.Value, Is.EqualTo(90L));
  }

  /// <summary>
  /// SCENARIO:
  /// Retention has been set to days/90
  ///
  /// ACTION:
  /// PUT /api/v1/retention with mode=percent, value=85
  ///
  /// EXPECTED RESULT:
  /// 200, and subsequent GET returns the overwritten mode=percent, value=85
  /// </summary>
  [Test]
  public async Task SetRetention_OverwritesPreviousPolicy()
  {
    await _client.PutAsJsonAsync("/api/v1/retention",
      new { mode = "percent", value = 85L });

    var body = (await ApiTestFixture.Envelope<RetentionPolicy>(
      await _client.GetAsync("/api/v1/retention"))).Body!;
    Assert.That(body.Mode, Is.EqualTo("percent"));
    Assert.That(body.Value, Is.EqualTo(85L));
  }
}
