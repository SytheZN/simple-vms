using Shared.Models;
using Shared.Models.Dto;

namespace Client.Core.Api;

public readonly record struct HttpError(Error Error, HttpDiagnostics? Diagnostics);

public interface IEnrollmentClient
{
  Task<OneOf<EnrollResponse, HttpError>> EnrollAsync(
    string serverAddress, string token, CancellationToken ct);
}
