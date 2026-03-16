using Shared.Models;

namespace Server.Api.Middleware;

public sealed class SetupState
{
  public bool SetupComplete { get; set; }
}

public sealed class SetupGateMiddleware
{
  private readonly RequestDelegate _next;

  public SetupGateMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    var state = context.RequestServices.GetRequiredService<SetupState>();

    if (state.SetupComplete)
    {
      if (context.Request.Path.StartsWithSegments("/api/v1/setup"))
      {
        context.Response.StatusCode = 404;
        return;
      }

      await _next(context);
      return;
    }

    if (context.Request.Path.StartsWithSegments("/api/v1/setup"))
    {
      await _next(context);
      return;
    }

    var envelope = new ResponseEnvelope
    {
      Result = Result.Unavailable,
      DebugTag = new DebugTag(ModuleIds.Setup, 0x0001),
      Message = "Server is in setup mode. Complete initial setup first."
    };

    context.Response.StatusCode = Result.Unavailable.ToHttpStatusCode();
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync(
      System.Text.Json.JsonSerializer.Serialize(envelope, ApiResponse.SerializerOptions),
      context.RequestAborted);
  }
}
