namespace Shared.Models;

public sealed class Credentials
{
  public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

  public string? Get(string key) =>
    Values.TryGetValue(key, out var value) ? value : null;

  public static Credentials FromUserPass(string username, string password) =>
    new() { Values = new Dictionary<string, string> { ["username"] = username, ["password"] = password } };
}
