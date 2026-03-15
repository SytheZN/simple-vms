using Microsoft.AspNetCore.Http;

namespace Shared.Models;

public interface IAuthProvider
{
  Task<AuthResult> AuthenticateAsync(HttpContext context, CancellationToken ct);
  Task ChallengeAsync(HttpContext context, CancellationToken ct);
}

public sealed class AuthResult
{
  public required bool Authenticated { get; init; }
  public string? Identity { get; init; }
  public IReadOnlyDictionary<string, string>? Claims { get; init; }
}
