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

  public static IResult Ok<T>(OneOf<T, Error> result, DebugTag successTag) =>
    result.Match(
      value => ToJsonResult(new ResponseEnvelope
      {
        Result = Result.Success,
        DebugTag = successTag,
        Body = value
      }),
      ToErrorResult);

  public static IResult Ok(OneOf<Success, Error> result, DebugTag successTag) =>
    result.Match(
      _ => ToJsonResult(new ResponseEnvelope
      {
        Result = Result.Success,
        DebugTag = successTag
      }),
      ToErrorResult);

  public static IResult Created<T>(OneOf<T, Error> result, DebugTag successTag) =>
    result.Match(
      value => ToJsonResult(new ResponseEnvelope
      {
        Result = Result.Created,
        DebugTag = successTag,
        Body = value
      }),
      ToErrorResult);

  public static IResult Err(Error error) => ToErrorResult(error);

  private static IResult ToErrorResult(Error error) =>
    ToJsonResult(new ResponseEnvelope
    {
      Result = error.Result,
      DebugTag = error.Tag,
      Message = error.Message
    });

  private static IResult ToJsonResult(ResponseEnvelope envelope) =>
    Results.Json(envelope, SerializerOptions, statusCode: envelope.Result.ToHttpStatusCode());

  private sealed class DebugTagJsonConverter : JsonConverter<DebugTag>
  {
    public override DebugTag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      new(uint.Parse(reader.GetString()![2..], System.Globalization.NumberStyles.HexNumber));

    public override void Write(Utf8JsonWriter writer, DebugTag value, JsonSerializerOptions options) =>
      writer.WriteStringValue(value.ToString());
  }
}
