using Shared.Models;

namespace Server.Api.Middleware;

public sealed class AuthMiddleware
{
  private readonly RequestDelegate _next;

  public AuthMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    var authProvider = context.RequestServices.GetService<IAuthProvider>();

    if (authProvider == null)
    {
      await _next(context);
      return;
    }

    if (context.Request.Path.StartsWithSegments("/api/v1/enroll"))
    {
      await _next(context);
      return;
    }

    var result = await authProvider.AuthenticateAsync(context, context.RequestAborted);
    if (!result.Authenticated)
    {
      await authProvider.ChallengeAsync(context, context.RequestAborted);
      return;
    }

    context.Items["Identity"] = result.Identity;
    context.Items["Claims"] = result.Claims;
    await _next(context);
  }
}
