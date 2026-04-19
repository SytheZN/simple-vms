using System.Text.Json;
using Server.Core;

namespace Server.Api.Middleware;

public sealed class ConfigurationRequiredMiddleware
{
  private readonly RequestDelegate _next;

  public ConfigurationRequiredMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context, SystemHealth health)
  {
    var path = context.Request.Path.Value;
    if (path == null || !IsGated(path))
    {
      await _next(context);
      return;
    }

    var reason = ClassifyReason(health);
    if (reason == null)
    {
      await _next(context);
      return;
    }

    var missing = reason == "missing-settings" ? health.MissingSettings : null;

    context.Response.StatusCode = StatusCodes.Status412PreconditionFailed;
    context.Response.ContentType = "application/json";
    await JsonSerializer.SerializeAsync(
      context.Response.Body,
      new ConfigurationRequiredBody("configuration-required", reason, missing),
      ConfigurationRequiredJsonContext.Default.ConfigurationRequiredBody,
      context.RequestAborted);
  }

  private static bool IsGated(string path)
  {
    if (!path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase)) return false;
    if (path.StartsWith("/api/v1/system/", StringComparison.OrdinalIgnoreCase)) return false;
    if (path.StartsWith("/api/v1/plugins", StringComparison.OrdinalIgnoreCase)) return false;
    return true;
  }

  private static string? ClassifyReason(SystemHealth health) => health.Status switch
  {
    "missing-certs" => "missing-certs",
    "starting" => "starting",
    "degraded" => "data-provider-unavailable",
    "healthy" when health.MissingSettings is { Length: > 0 } => "missing-settings",
    _ => null
  };
}

public sealed record ConfigurationRequiredBody(
  string Error,
  string Reason,
  string[]? Missing);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
  PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(ConfigurationRequiredBody))]
internal partial class ConfigurationRequiredJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
