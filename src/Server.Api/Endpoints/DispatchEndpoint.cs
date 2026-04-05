using System.Text.Json;
using Server.Core;
using Server.Core.Routing;
using Shared.Models;

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
      BodyBytes = bodyBytes
    };

    Task<ResponseEnvelope>? result;
    try
    {
      result = dispatcher.TryDispatch(request, ct);
      if (result != null)
      {
        var envelope = await result;
        return Results.Json(envelope, ServerJsonContext.Default.ResponseEnvelope,
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
        ServerJsonContext.Default.ResponseEnvelope,
        statusCode: 400);
    }

    return Results.Json(
      new ResponseEnvelope
      {
        Result = Result.NotFound,
        DebugTag = new DebugTag(ModuleIds.Api, 0x0002),
        Message = $"No route for {request.Method} {request.Path}"
      },
      ServerJsonContext.Default.ResponseEnvelope,
      statusCode: 404);
  }
}
