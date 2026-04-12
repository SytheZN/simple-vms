using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Dto;

namespace Client.Core.Api;

public sealed class EnrollmentClient : IEnrollmentClient
{
  private readonly IHttpClientFactory _httpFactory;
  private readonly ILogger<EnrollmentClient> _logger;

  public EnrollmentClient(IHttpClientFactory httpFactory, ILogger<EnrollmentClient> logger)
  {
    _httpFactory = httpFactory;
    _logger = logger;
  }

  public async Task<OneOf<EnrollResponse, HttpError>> EnrollAsync(
    string serverAddress, string token, CancellationToken ct)
  {
    var request = new EnrollRequest { Token = token };
    var baseUri = serverAddress.Contains("://")
      ? new Uri(serverAddress)
      : new Uri($"http://{serverAddress}");
    var url = new Uri(baseUri, "/api/v1/enroll").ToString();
    _logger.LogDebug("Enrolling at {Url}", url);

    HttpResponseMessage response;
    try
    {
      using var http = _httpFactory.CreateClient();
      response = await http.PostAsJsonAsync(url, request, ClientJsonContext.Default.EnrollRequest, ct);
    }
    catch (HttpRequestException ex)
    {
      _logger.LogError(ex, "HTTP request to {Url} failed", url);
      return new HttpError(
        Error.Create(ClientModuleIds.Enrollment, 0x0001, Result.Unavailable, ex.Message),
        new HttpDiagnostics(url, null, null));
    }
    catch (TaskCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (TaskCanceledException)
    {
      _logger.LogWarning("Enrollment request to {Url} timed out", url);
      return new HttpError(
        Error.Create(ClientModuleIds.Enrollment, 0x0004, Result.Unavailable, "Enrollment request timed out"),
        new HttpDiagnostics(url, null, null));
    }

    var rawBody = await response.Content.ReadAsStringAsync(ct);
    var diag = new HttpDiagnostics(url, (int)response.StatusCode, rawBody);

    ResponseEnvelope? envelope;
    try
    {
      envelope = JsonSerializer.Deserialize(rawBody, ClientJsonContext.Default.ResponseEnvelope);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to parse response from {Url}, status={Status}, body={Body}",
        url, (int)response.StatusCode, rawBody);
      return new HttpError(
        Error.Create(ClientModuleIds.Enrollment, 0x0002, Result.InternalError,
          $"Server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})"),
        diag);
    }

    if (envelope == null)
      return new HttpError(
        Error.Create(ClientModuleIds.Enrollment, 0x0002, Result.InternalError, "Empty response from server"),
        diag);

    if (envelope.Result != Result.Success && envelope.Result != Result.Created)
    {
      _logger.LogWarning("Enrollment failed: {Result} {Tag} {Message}",
        envelope.Result, envelope.DebugTag, envelope.Message);
      return new HttpError(
        new Error(envelope.Result, envelope.DebugTag, envelope.Message ?? "Enrollment failed"),
        diag);
    }

    if (envelope.Body == null)
      return new HttpError(
        Error.Create(ClientModuleIds.Enrollment, 0x0003, Result.InternalError,
          "Server returned success but no enrollment data"),
        diag);

    var enrollResponse = envelope.Body.Value.Deserialize(ClientJsonContext.Default.EnrollResponse);
    if (enrollResponse == null)
      return new HttpError(
        Error.Create(ClientModuleIds.Enrollment, 0x0003, Result.InternalError,
          "Failed to parse enrollment response"),
        diag);

    _logger.LogInformation("Enrollment succeeded, clientId={ClientId}", enrollResponse.ClientId);
    return enrollResponse;
  }
}
