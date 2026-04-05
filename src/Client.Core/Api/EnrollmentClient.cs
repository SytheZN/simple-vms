using System.Net.Http.Json;
using Shared.Models;
using Shared.Models.Dto;

namespace Client.Core.Api;

public sealed class EnrollmentClient : IEnrollmentClient
{
  private readonly IHttpClientFactory _httpFactory;

  public EnrollmentClient(IHttpClientFactory httpFactory)
  {
    _httpFactory = httpFactory;
  }

  public async Task<OneOf<EnrollResponse, Error>> EnrollAsync(
    string serverAddress, string token, CancellationToken ct)
  {
    var request = new EnrollRequest { Token = token };
    var host = serverAddress.Contains("://") ? serverAddress : $"http://{serverAddress}";
    var url = $"{host}/api/v1/enroll";

    try
    {
      using var http = _httpFactory.CreateClient();

      var response = await http.PostAsJsonAsync(url, request, ClientJsonContext.Default.EnrollRequest, ct);

      if (!response.IsSuccessStatusCode)
      {
        var body = await response.Content.ReadAsStringAsync(ct);
        var errorResult = (int)response.StatusCode switch
        {
          400 => Result.BadRequest,
          401 => Result.Unauthorized,
          403 => Result.Forbidden,
          404 => Result.NotFound,
          409 => Result.Conflict,
          >= 500 => Result.InternalError,
          _ => Result.BadRequest
        };
        return Error.Create(ClientModuleIds.Enrollment, 0x0002, errorResult,
          $"Enrollment failed ({response.StatusCode}): {body}");
      }

      var result = await response.Content.ReadFromJsonAsync(ClientJsonContext.Default.EnrollResponse, ct);
      if (result == null)
        return Error.Create(ClientModuleIds.Enrollment, 0x0003, Result.InternalError, "Failed to parse enrollment response");

      return result;
    }
    catch (HttpRequestException ex)
    {
      return Error.Create(ClientModuleIds.Enrollment, 0x0001, Result.Unavailable, ex.Message);
    }
    catch (TaskCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (TaskCanceledException)
    {
      return Error.Create(ClientModuleIds.Enrollment, 0x0004, Result.Unavailable, "Enrollment request timed out");
    }
  }
}
