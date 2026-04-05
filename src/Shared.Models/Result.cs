using System.Text.Json.Serialization;

namespace Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter<Result>))]
public enum Result
{
  Success,
  Created,
  NotFound,
  BadRequest,
  Conflict,
  Unauthorized,
  Forbidden,
  InternalError,
  Unavailable
}
