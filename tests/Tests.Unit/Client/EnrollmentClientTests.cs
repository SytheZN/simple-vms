using System.Net;
using System.Text.Json;
using Client.Core.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client;

[TestFixture]
public class EnrollmentClientTests
{
  /// <summary>
  /// SCENARIO:
  /// The enrollment API returns a valid credential bundle
  ///
  /// ACTION:
  /// Call EnrollAsync with a valid address and token
  ///
  /// EXPECTED RESULT:
  /// Returns the EnrollResponse with addresses, certs, and clientId
  /// </summary>
  [Test]
  public async Task Enroll_ValidResponse_ReturnsCredentials()
  {
    var clientId = Guid.NewGuid();
    var expected = new EnrollResponse
    {
      Addresses = ["192.168.1.50:4433"],
      Ca = "-----BEGIN CERTIFICATE-----\nCA\n-----END CERTIFICATE-----",
      Cert = "-----BEGIN CERTIFICATE-----\nCERT\n-----END CERTIFICATE-----",
      Key = "-----BEGIN PRIVATE KEY-----\nKEY\n-----END PRIVATE KEY-----",
      ClientId = clientId
    };

    var envelope = new ResponseEnvelope
    {
      Result = Result.Success,
      DebugTag = default,
      Body = JsonSerializer.SerializeToElement(expected, ClientJsonContext.Default.EnrollResponse)
    };
    var responseJson = JsonSerializer.SerializeToUtf8Bytes(
      envelope, ClientJsonContext.Default.ResponseEnvelope);
    var handler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ByteArrayContent(responseJson)
      {
        Headers = { ContentType = new("application/json") }
      }
    });

    var factory = new FakeHttpClientFactory(handler);
    var client = new EnrollmentClient(factory, NullLogger<EnrollmentClient>.Instance);

    var result = await client.EnrollAsync("localhost:8080", "A7F2-9K4X", CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(result.AsT0.ClientId, Is.EqualTo(clientId));
    Assert.That(result.AsT0.Addresses, Has.Length.EqualTo(1));
    Assert.That(result.AsT0.Ca, Does.Contain("CA"));
  }

  /// <summary>
  /// SCENARIO:
  /// The enrollment API returns a 401 Unauthorized
  ///
  /// ACTION:
  /// Call EnrollAsync with an invalid token
  ///
  /// EXPECTED RESULT:
  /// Returns Error with Unauthorized result
  /// </summary>
  [Test]
  public async Task Enroll_Unauthorized_ReturnsError()
  {
    var envelope = new ResponseEnvelope
    {
      Result = Result.Unauthorized,
      DebugTag = default,
      Message = "Invalid token"
    };
    var responseJson = JsonSerializer.SerializeToUtf8Bytes(
      envelope, ClientJsonContext.Default.ResponseEnvelope);
    var handler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
      Content = new ByteArrayContent(responseJson)
      {
        Headers = { ContentType = new("application/json") }
      }
    });

    var factory = new FakeHttpClientFactory(handler);
    var client = new EnrollmentClient(factory, NullLogger<EnrollmentClient>.Instance);

    var result = await client.EnrollAsync("localhost:8080", "BAD-TOKEN", CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Error.Result, Is.EqualTo(Result.Unauthorized));
    Assert.That(result.AsT1.Error.Message, Does.Contain("Invalid token"));
  }

  /// <summary>
  /// SCENARIO:
  /// The server is unreachable
  ///
  /// ACTION:
  /// Call EnrollAsync when the HTTP request throws
  ///
  /// EXPECTED RESULT:
  /// Returns Error with Unavailable result
  /// </summary>
  [Test]
  public async Task Enroll_ConnectionRefused_ReturnsUnavailable()
  {
    var handler = new FakeHttpHandler(new HttpRequestException("Connection refused"));

    var factory = new FakeHttpClientFactory(handler);
    var client = new EnrollmentClient(factory, NullLogger<EnrollmentClient>.Instance);

    var result = await client.EnrollAsync("localhost:8080", "TOKEN", CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Error.Result, Is.EqualTo(Result.Unavailable));
  }

  /// <summary>
  /// SCENARIO:
  /// The request body is sent correctly
  ///
  /// ACTION:
  /// Call EnrollAsync and capture the request
  ///
  /// EXPECTED RESULT:
  /// The POST body contains the token in JSON format
  /// </summary>
  [Test]
  public async Task Enroll_SendsCorrectPostBody()
  {
    var validResponse = new EnrollResponse
    {
      Addresses = ["localhost:4433"],
      Ca = "ca", Cert = "cert", Key = "key",
      ClientId = Guid.NewGuid()
    };
    var envelope = new ResponseEnvelope
    {
      Result = Result.Success,
      DebugTag = default,
      Body = JsonSerializer.SerializeToElement(validResponse, ClientJsonContext.Default.EnrollResponse)
    };
    var responseJson = JsonSerializer.SerializeToUtf8Bytes(
      envelope, ClientJsonContext.Default.ResponseEnvelope);
    var handler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ByteArrayContent(responseJson)
      {
        Headers = { ContentType = new("application/json") }
      }
    });

    var factory = new FakeHttpClientFactory(handler);
    var client = new EnrollmentClient(factory, NullLogger<EnrollmentClient>.Instance);

    await client.EnrollAsync("myserver:8080", "TEST-TOKEN", CancellationToken.None);

    Assert.That(handler.LastRequest, Is.Not.Null);
    Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Post));
    Assert.That(handler.LastRequest.RequestUri!.ToString(),
      Is.EqualTo("http://myserver:8080/api/v1/enroll"));

    var body = await handler.LastRequest.Content!.ReadAsStringAsync();
    var doc = JsonDocument.Parse(body);
    Assert.That(doc.RootElement.GetProperty("token").GetString(), Is.EqualTo("TEST-TOKEN"));
  }
}
