using System.Text.Json;
using System.Text.Json.Serialization;

namespace Data.Sqlite;

[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class SqliteJsonContext : JsonSerializerContext;

internal static class SqliteJsonExtensions
{
  public static string ToJson(this string[] value) =>
    JsonSerializer.Serialize(value, SqliteJsonContext.Default.StringArray);

  public static string ToJson(this Dictionary<string, string> value) =>
    JsonSerializer.Serialize(value, SqliteJsonContext.Default.DictionaryStringString);

  public static string[] ToStringArray(this string json) =>
    JsonSerializer.Deserialize(json, SqliteJsonContext.Default.StringArray) ?? [];

  public static Dictionary<string, string> ToStringDictionary(this string json) =>
    JsonSerializer.Deserialize(json, SqliteJsonContext.Default.DictionaryStringString) ?? [];

  public static Dictionary<string, string>? ToStringDictionaryOrNull(this string json) =>
    JsonSerializer.Deserialize(json, SqliteJsonContext.Default.DictionaryStringString);
}
