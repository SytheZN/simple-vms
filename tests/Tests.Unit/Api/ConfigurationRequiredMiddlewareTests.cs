using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Server.Api.Middleware;
using Server.Core;

namespace Tests.Unit.Api;

[TestFixture]
public class ConfigurationRequiredMiddlewareTests
{
  /// <summary>
  /// SCENARIO:
  /// A request arrives on a path that is inside the always-reachable whitelist
  /// (/api/v1/system/* or /api/v1/plugins*) while the server is in a state that
  /// would otherwise gate requests
  ///
  /// ACTION:
  /// Middleware pipeline runs
  ///
  /// EXPECTED RESULT:
  /// The request is forwarded to the next delegate without any 412 body
  /// </summary>
  [TestCase("/api/v1/system/health")]
  [TestCase("/api/v1/plugins")]
  [TestCase("/api/v1/plugins/data")]
  [TestCase("/setup")]
  [TestCase("/")]
  public async Task Whitelisted_PassesThrough(string path)
  {
    var health = new SystemHealth();
    var context = BuildContext(path);
    var called = false;
    var middleware = new ConfigurationRequiredMiddleware(_ => { called = true; return Task.CompletedTask; });

    await middleware.InvokeAsync(context, health);

    Assert.That(called, Is.True);
    Assert.That(context.Response.StatusCode, Is.EqualTo(200));
  }

  /// <summary>
  /// SCENARIO:
  /// A gated API request arrives while the server is in each possible
  /// pre-ready state (no certs, starting, degraded, or healthy with missing
  /// settings)
  ///
  /// ACTION:
  /// Middleware pipeline runs
  ///
  /// EXPECTED RESULT:
  /// Request is short-circuited with a 412 and a configuration-required body
  /// whose reason matches the state
  /// </summary>
  [TestCase("missing-certs", false, "missing-certs")]
  [TestCase("starting", false, "starting")]
  [TestCase("degraded", false, "data-provider-unavailable")]
  [TestCase("healthy", true, "missing-settings")]
  public async Task GatedRequest_ReturnsReasonForState(
    string status, bool withMissing, string expectedReason)
  {
    var health = SetHealth(status, withMissing ? ["internalEndpoint"] : null);
    var context = BuildContext("/api/v1/cameras");
    var called = false;
    var middleware = new ConfigurationRequiredMiddleware(_ => { called = true; return Task.CompletedTask; });

    await middleware.InvokeAsync(context, health);

    Assert.That(called, Is.False);
    Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status412PreconditionFailed));

    var body = await ReadBody(context);
    Assert.That(body.GetProperty("error").GetString(), Is.EqualTo("configuration-required"));
    Assert.That(body.GetProperty("reason").GetString(), Is.EqualTo(expectedReason));
  }

  /// <summary>
  /// SCENARIO:
  /// Server is healthy with one or more missing settings
  ///
  /// ACTION:
  /// Gated request reaches the middleware
  ///
  /// EXPECTED RESULT:
  /// The response body includes the missing keys; for non-missing-settings
  /// reasons the missing field is omitted
  /// </summary>
  [Test]
  public async Task MissingSettings_IncludedOnlyWhenApplicable()
  {
    var health = SetHealth("healthy", ["internalEndpoint", "legacyExternalEndpoint"]);
    var context = BuildContext("/api/v1/cameras");
    var middleware = new ConfigurationRequiredMiddleware(_ => Task.CompletedTask);
    await middleware.InvokeAsync(context, health);

    var body = await ReadBody(context);
    var missing = body.GetProperty("missing").EnumerateArray().Select(e => e.GetString()).ToArray();
    Assert.That(missing, Is.EquivalentTo(new[] { "internalEndpoint", "legacyExternalEndpoint" }));

    var degradedHealth = SetHealth("degraded", null);
    var degradedContext = BuildContext("/api/v1/cameras");
    await middleware.InvokeAsync(degradedContext, degradedHealth);
    var degradedBody = await ReadBody(degradedContext);
    Assert.That(degradedBody.GetProperty("missing").ValueKind, Is.EqualTo(JsonValueKind.Null));
  }

  /// <summary>
  /// SCENARIO:
  /// Server is healthy with no missing settings
  ///
  /// ACTION:
  /// Gated request reaches the middleware
  ///
  /// EXPECTED RESULT:
  /// Request passes through - the pipeline is open for normal traffic
  /// </summary>
  [Test]
  public async Task HealthyAndComplete_PassesThrough()
  {
    var health = SetHealth("healthy", null);
    var context = BuildContext("/api/v1/cameras");
    var called = false;
    var middleware = new ConfigurationRequiredMiddleware(_ => { called = true; return Task.CompletedTask; });

    await middleware.InvokeAsync(context, health);

    Assert.That(called, Is.True);
  }

  private static SystemHealth SetHealth(string status, string[]? missing)
  {
    var health = new SystemHealth();
    switch (status)
    {
      case "starting": health.TransitionToStarting(); break;
      case "degraded": health.TransitionToDegraded(); break;
      case "healthy": health.TransitionToHealthy(); break;
    }
    health.SetMissingSettings(missing);
    return health;
  }

  private static DefaultHttpContext BuildContext(string path)
  {
    var context = new DefaultHttpContext();
    context.Request.Path = path;
    context.Response.Body = new MemoryStream();
    return context;
  }

  private static async Task<JsonElement> ReadBody(HttpContext context)
  {
    context.Response.Body.Position = 0;
    using var doc = await JsonDocument.ParseAsync(context.Response.Body);
    return doc.RootElement.Clone();
  }
}
