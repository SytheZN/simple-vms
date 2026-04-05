using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Core;

[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class CredentialsJsonContext : JsonSerializerContext;

public static class CredentialsJsonExtensions
{
  public static byte[] ToCredentialsJson(this IReadOnlyDictionary<string, string> value) =>
    JsonSerializer.SerializeToUtf8Bytes(value, CredentialsJsonContext.Default.IReadOnlyDictionaryStringString);

  public static IReadOnlyDictionary<string, string>? ParseCredentials(this byte[] json) =>
    JsonSerializer.Deserialize(json, CredentialsJsonContext.Default.IReadOnlyDictionaryStringString);

  public static Dictionary<string, string>? ParseCredentialsDictionary(this byte[] json) =>
    JsonSerializer.Deserialize(json, CredentialsJsonContext.Default.DictionaryStringString);
}
