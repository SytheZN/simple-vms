using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Shared.Models;

namespace Server.Core.Routing;

public static class ApiResult
{
  public static ResponseEnvelope Ok<T>(
    OneOf<T, Error> result, DebugTag successTag, JsonTypeInfo<T> typeInfo) =>
    result.Match(
      value => new ResponseEnvelope
      {
        Result = Result.Success,
        DebugTag = successTag,
        Body = ToJsonElement(value, typeInfo)
      },
      ToError);

  public static ResponseEnvelope Ok(OneOf<Success, Error> result, DebugTag successTag) =>
    result.Match(
      _ => new ResponseEnvelope
      {
        Result = Result.Success,
        DebugTag = successTag
      },
      ToError);

  public static ResponseEnvelope Created<T>(
    OneOf<T, Error> result, DebugTag successTag, JsonTypeInfo<T> typeInfo) =>
    result.Match(
      value => new ResponseEnvelope
      {
        Result = Result.Created,
        DebugTag = successTag,
        Body = ToJsonElement(value, typeInfo)
      },
      ToError);

  public static ResponseEnvelope Success(DebugTag tag) =>
    new() { Result = Result.Success, DebugTag = tag };

  public static ResponseEnvelope Success<T>(T body, DebugTag tag, JsonTypeInfo<T> typeInfo) =>
    new() { Result = Result.Success, DebugTag = tag, Body = ToJsonElement(body, typeInfo) };

  public static ResponseEnvelope Err(Error error) =>
    new() { Result = error.Result, DebugTag = error.Tag, Message = error.Message };

  public static ResponseEnvelope Err(Result result, DebugTag tag, string message) =>
    new() { Result = result, DebugTag = tag, Message = message };

  private static ResponseEnvelope ToError(Error error) =>
    new() { Result = error.Result, DebugTag = error.Tag, Message = error.Message };

  private static JsonElement? ToJsonElement<T>(T value, JsonTypeInfo<T> typeInfo)
  {
    if (value == null) return null;
    var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
    using var doc = JsonDocument.Parse(bytes);
    return doc.RootElement.Clone();
  }
}
