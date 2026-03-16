using System.Text.Json.Serialization;

namespace Shared.Models.Dto;

[JsonSerializable(typeof(QrPayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class EnrollmentJsonContext : JsonSerializerContext;
