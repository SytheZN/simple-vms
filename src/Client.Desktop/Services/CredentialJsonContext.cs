using System.Text.Json.Serialization;
using Client.Core.Platform;

namespace Client.Desktop.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CredentialData))]
internal sealed partial class CredentialJsonContext : JsonSerializerContext;
