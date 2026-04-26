using System.Text.Json.Serialization;

namespace Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter<StreamKind>))]
public enum StreamKind
{
  [JsonStringEnumMemberName("quality")] Quality,
  [JsonStringEnumMemberName("metadata")] Metadata
}
