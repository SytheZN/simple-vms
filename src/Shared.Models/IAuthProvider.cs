using Microsoft.AspNetCore.Http;

namespace Shared.Models;

public interface IAuthProvider
{
  Task<AuthResult> AuthenticateAsync(HttpContext context, CancellationToken ct);
  Task ChallengeAsync(HttpContext context, CancellationToken ct);
}
