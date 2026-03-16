using Server.Api.Endpoints;
using Server.Api.Middleware;
using Server.Core;
using Server.Core.Services;

namespace Server.Api;

public static class ApiExtensions
{
  public static IServiceCollection AddApiServices(this IServiceCollection services)
  {
    services.AddSingleton<SetupState>();
    services.AddSingleton<CameraStatusTracker>();
    services.AddSingleton<ConnectionTracker>();
    services.AddSingleton<EnrollmentService>();
    services.AddSingleton<CameraService>();
    services.AddSingleton<ClientService>();
    services.AddSingleton<DiscoveryService>();
    services.AddSingleton<RecordingService>();
    services.AddSingleton<EventService>();
    services.AddSingleton<RetentionService>();
    services.AddSingleton<SystemService>();
    services.AddSingleton<PluginService>();
    return services;
  }

  public static WebApplication UseApiMiddleware(this WebApplication app)
  {
    app.UseMiddleware<SetupGateMiddleware>();
    app.UseMiddleware<AuthMiddleware>();
    app.UseMiddleware<AuthzMiddleware>();
    return app;
  }

  public static WebApplication MapApiEndpoints(this WebApplication app)
  {
    EnrollmentEndpoints.Map(app);
    CameraEndpoints.Map(app);
    ClientEndpoints.Map(app);
    DiscoveryEndpoints.Map(app);
    RecordingEndpoints.Map(app);
    EventEndpoints.Map(app);
    RetentionEndpoints.Map(app);
    SystemEndpoints.Map(app);
    PluginEndpoints.Map(app);
    return app;
  }
}
