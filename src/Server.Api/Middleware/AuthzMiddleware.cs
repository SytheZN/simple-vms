using System.Text.Json;
using Server.Core;
using Server.Plugins;
using Shared.Models;

namespace Server.Api.Middleware;

public sealed class AuthzMiddleware
{
  private readonly RequestDelegate _next;

  public AuthzMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    var pluginHost = context.RequestServices.GetRequiredService<IPluginHost>();
    var authzProvider = pluginHost.AuthzProviders.FirstOrDefault();

    if (authzProvider == null)
    {
      await _next(context);
      return;
    }

    var identity = context.Items["Identity"] as string;
    var operation = $"{context.Request.Method} {context.Request.Path}";

    var authorized = await authzProvider.AuthorizeAsync(
      identity, operation, null, context.RequestAborted);

    if (authorized)
    {
      await _next(context);
      return;
    }

    var envelope = new ResponseEnvelope
    {
      Result = Result.Forbidden,
      DebugTag = new DebugTag(ModuleIds.Api, 0x0001),
      Message = "Access denied"
    };

    context.Response.StatusCode = Result.Forbidden.ToHttpStatusCode();
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync(
      JsonSerializer.Serialize(envelope, ServerJsonContext.Default.ResponseEnvelope),
      context.RequestAborted);
  }
}
