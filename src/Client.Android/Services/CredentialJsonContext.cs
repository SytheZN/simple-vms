using System.Text.Json.Serialization;
using Client.Core.Platform;

namespace Client.Android.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CredentialData))]
internal sealed partial class CredentialJsonContext : JsonSerializerContext;
