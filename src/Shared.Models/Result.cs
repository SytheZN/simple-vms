using System.Text.Json.Serialization;

namespace Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter<Result>))]
public enum Result
{
  [JsonStringEnumMemberName("success")] Success,
  [JsonStringEnumMemberName("created")] Created,
  [JsonStringEnumMemberName("notFound")] NotFound,
  [JsonStringEnumMemberName("badRequest")] BadRequest,
  [JsonStringEnumMemberName("conflict")] Conflict,
  [JsonStringEnumMemberName("unauthorized")] Unauthorized,
  [JsonStringEnumMemberName("forbidden")] Forbidden,
  [JsonStringEnumMemberName("internalError")] InternalError,
  [JsonStringEnumMemberName("unavailable")] Unavailable
}
