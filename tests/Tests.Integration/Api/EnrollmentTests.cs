using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class EnrollmentTests
{
  private HttpClient _client = null!;

  [OneTimeSetUp]
  public void Setup() => _client = ApiTestFixture.Client;

  /// <summary>
  /// SCENARIO:
  /// Server is running with no pending tokens
  ///
  /// ACTION:
  /// POST /api/v1/clients/enroll
  ///
  /// EXPECTED RESULT:
  /// 201 with token in XXXX-XXXX format using only the safe character set
  /// </summary>
  [Test]
  public async Task StartEnrollment_ReturnsToken()
  {
    var response = await _client.PostAsync("/api/v1/clients/enroll", null);
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

    var envelope = await ApiTestFixture.Envelope<StartEnrollmentResponse>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Created));
    Assert.That(envelope.Body!.Token, Does.Match(@"^[A-Z2-9]{4}-[A-Z2-9]{4}$"));
  }

  /// <summary>
  /// SCENARIO:
  /// Server is running with no pending tokens
  ///
  /// ACTION:
  /// POST /api/v1/clients/enroll
  ///
  /// EXPECTED RESULT:
  /// Response contains qrData that is valid JSON with v=1, addresses array, and the token
  /// </summary>
  [Test]
  public async Task StartEnrollment_QrDataContainsValidPayload()
  {
    var response = await _client.PostAsync("/api/v1/clients/enroll", null);
    var body = (await ApiTestFixture.Envelope<StartEnrollmentResponse>(response)).Body!;

    var qr = JsonSerializer.Deserialize<QrPayload>(body.QrData,
      EnrollmentJsonContext.Default.QrPayload)!;

    Assert.That(qr.V, Is.EqualTo(1));
    Assert.That(qr.Token, Is.EqualTo(body.Token));
    Assert.That(qr.Addresses, Is.Not.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A valid enrollment token has been generated
  ///
  /// ACTION:
  /// POST /api/v1/enroll with the token
  ///
  /// EXPECTED RESULT:
  /// 200 with PEM-encoded CA cert, client cert, private key, valid GUID clientId, and tunnel addresses
  /// </summary>
  [Test]
  public async Task CompleteEnrollment_ReturnsCredentials()
  {
    var start = (await ApiTestFixture.Envelope<StartEnrollmentResponse>(
      await _client.PostAsync("/api/v1/clients/enroll", null))).Body!;

    var response = await _client.PostAsJsonAsync("/api/v1/enroll",
      new { token = start.Token });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<EnrollResponse>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));

    var body = envelope.Body!;
    Assert.That(body.Ca, Does.StartWith("-----BEGIN CERTIFICATE-----"));
    Assert.That(body.Cert, Does.StartWith("-----BEGIN CERTIFICATE-----"));
    Assert.That(body.Key, Does.StartWith("-----BEGIN RSA PRIVATE KEY-----"));
    Assert.That(body.ClientId, Is.Not.EqualTo(Guid.Empty));
    Assert.That(body.Addresses, Is.Not.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A token has already been consumed by a successful enrollment
  ///
  /// ACTION:
  /// POST /api/v1/enroll with the same token again
  ///
  /// EXPECTED RESULT:
  /// 404 because tokens are single-use
  /// </summary>
  [Test]
  public async Task CompleteEnrollment_TokenIsSingleUse()
  {
    var start = (await ApiTestFixture.Envelope<StartEnrollmentResponse>(
      await _client.PostAsync("/api/v1/clients/enroll", null))).Body!;

    var first = await _client.PostAsJsonAsync("/api/v1/enroll",
      new { token = start.Token });
    Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var second = await _client.PostAsJsonAsync("/api/v1/enroll",
      new { token = start.Token });
    Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// No pending enrollment tokens exist
  ///
  /// ACTION:
  /// POST /api/v1/enroll with a fabricated token
  ///
  /// EXPECTED RESULT:
  /// 404 with message mentioning the token is invalid or expired
  /// </summary>
  [Test]
  public async Task CompleteEnrollment_InvalidTokenReturnsNotFound()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/enroll",
      new { token = "ZZZZ-ZZZZ" });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
    Assert.That(envelope.Message, Does.Contain("token"));
  }

  /// <summary>
  /// SCENARIO:
  /// Two enrollment tokens are generated independently
  ///
  /// ACTION:
  /// POST /api/v1/clients/enroll twice, then complete both
  ///
  /// EXPECTED RESULT:
  /// Both tokens work independently and produce different clientIds
  /// </summary>
  [Test]
  public async Task CompleteEnrollment_ConcurrentTokensAreIndependent()
  {
    var s1 = (await ApiTestFixture.Envelope<StartEnrollmentResponse>(
      await _client.PostAsync("/api/v1/clients/enroll", null))).Body!;
    var s2 = (await ApiTestFixture.Envelope<StartEnrollmentResponse>(
      await _client.PostAsync("/api/v1/clients/enroll", null))).Body!;

    Assert.That(s1.Token, Is.Not.EqualTo(s2.Token));

    var r1 = await ApiTestFixture.Envelope<EnrollResponse>(
      await _client.PostAsJsonAsync("/api/v1/enroll", new { token = s1.Token }));
    var r2 = await ApiTestFixture.Envelope<EnrollResponse>(
      await _client.PostAsJsonAsync("/api/v1/enroll", new { token = s2.Token }));

    Assert.That(r1.Body!.ClientId, Is.Not.EqualTo(r2.Body!.ClientId));
  }

  /// <summary>
  /// SCENARIO:
  /// A client has been enrolled via the full flow
  ///
  /// ACTION:
  /// GET /api/v1/clients/{clientId}
  ///
  /// EXPECTED RESULT:
  /// The client exists with correct id, a name, enrolledAt > 0, and connected = false
  /// </summary>
  [Test]
  public async Task CompleteEnrollment_ClientPersisted()
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
}
