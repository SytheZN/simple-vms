using Shared.Models;
using Shared.Models.Dto;

namespace Client.Core.Api;

public interface IEnrollmentClient
{
  Task<OneOf<EnrollResponse, Error>> EnrollAsync(
    string serverAddress, string token, CancellationToken ct);
}
