using System.Text.Json.Serialization;

namespace Server.Core;

[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class CredentialsJsonContext : JsonSerializerContext;
