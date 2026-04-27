using System.Text.Json.Serialization;

namespace Shared.Api;

[JsonConverter(typeof(JsonStringEnumConverter<RemoteAccessMode>))]
public enum RemoteAccessMode
{
  [JsonStringEnumMemberName("none")] None,
  [JsonStringEnumMemberName("manual")] Manual,
  [JsonStringEnumMemberName("upnp")] Upnp
}
