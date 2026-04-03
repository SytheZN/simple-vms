using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Models;

namespace Server.Api;

public static class ApiResponse
{
  public static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters =
    {
      new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
      new DebugTagJsonConverter()
    }
  };

  public static IResult Err(Error error) =>
    Results.Json(
      new ResponseEnvelope
      {
        Result = error.Result,
        DebugTag = error.Tag,
        Message = error.Message
      },
      SerializerOptions,
      statusCode: error.Result.ToHttpStatusCode());

  private sealed class DebugTagJsonConverter : JsonConverter<DebugTag>
  {
    public override DebugTag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      new(uint.Parse(reader.GetString()![2..], System.Globalization.NumberStyles.HexNumber));

    public override void Write(Utf8JsonWriter writer, DebugTag value, JsonSerializerOptions options) =>
      writer.WriteStringValue(value.ToString());
  }
}
