namespace Shared.Models;

public sealed class AuthResult
{
  public required bool Authenticated { get; init; }
  public string? Identity { get; init; }
  public IReadOnlyDictionary<string, string>? Claims { get; init; }
}
