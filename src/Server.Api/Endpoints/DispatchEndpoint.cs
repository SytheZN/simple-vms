using System.Text.Json;
using Server.Core.Routing;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Api.Endpoints;

public static class DispatchEndpoint
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.Map("/api/v1/{**path}", HandleAsync);
  }

  private static async Task<IResult> HandleAsync(
    HttpContext context,
    ApiDispatcher dispatcher,
    CancellationToken ct)
  {
    if (context.WebSockets.IsWebSocketRequest)
      return Results.BadRequest();

    byte[]? bodyBytes = null;
    if (context.Request.ContentLength > 0 ||
        context.Request.Headers.ContentType.Count > 0)
    {
      using var ms = new MemoryStream();
      await context.Request.Body.CopyToAsync(ms, ct);
      if (ms.Length > 0)
        bodyBytes = ms.ToArray();
    }

    var query = new Dictionary<string, string>();
    foreach (var (key, value) in context.Request.Query)
      query[key] = value.ToString();

    var request = new ApiRequest
    {
      Method = context.Request.Method,
      Path = context.Request.Path.Value ?? "/",
      Query = query,
      Services = context.RequestServices,
      BodyDeserializer = bodyBytes != null
        ? type => DeserializeJson(bodyBytes, type)
        : null
    };

    Task<ResponseEnvelope>? result;
    try
    {
      result = dispatcher.TryDispatch(request, ct);
      if (result != null)
      {
        var envelope = await result;
        return Results.Json(envelope, ApiResponse.SerializerOptions,
          statusCode: envelope.Result.ToHttpStatusCode());
      }
    }
    catch (Exception ex) when (
      ex is FormatException or KeyNotFoundException
        or InvalidOperationException or JsonException)
    {
      return Results.Json(
        new ResponseEnvelope
        {
          Result = Result.BadRequest,
          DebugTag = new DebugTag(ModuleIds.Api, 0x0003),
          Message = ex.Message
        },
        ApiResponse.SerializerOptions,
        statusCode: 400);
    }

    return Results.Json(
      new ResponseEnvelope
      {
        Result = Result.NotFound,
        DebugTag = new DebugTag(ModuleIds.Api, 0x0002),
        Message = $"No route for {request.Method} {request.Path}"
      },
      ApiResponse.SerializerOptions,
      statusCode: 404);
  }

  private static object? DeserializeJson(byte[] bytes, Type type)
  {
    if (type == typeof(Dictionary<string, object>))
      return DeserializeObjectDictionary(bytes);

    var result = JsonSerializer.Deserialize(bytes, type, ApiResponse.SerializerOptions);

    if (result is ValidateFieldRequest { Value: JsonElement el } vfr)
      return new ValidateFieldRequest { Key = vfr.Key, Value = UnwrapJsonElement(el) };

    return result;
  }

  private static Dictionary<string, object> DeserializeObjectDictionary(byte[] bytes)
  {
    var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
      bytes, ApiResponse.SerializerOptions);
    if (raw == null)
      throw new InvalidOperationException("Expected a JSON object body");
    return raw.ToDictionary(
      kvp => kvp.Key,
      kvp => UnwrapJsonElement(kvp.Value));
  }

  private static object UnwrapJsonElement(JsonElement element) => element.ValueKind switch
  {
    JsonValueKind.String => element.GetString()!,
    JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.Null => null!,
    _ => element.ToString()
  };
}
