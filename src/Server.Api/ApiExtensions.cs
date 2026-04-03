using Server.Api.Endpoints;
using Server.Api.Middleware;
using Server.Core;
using Server.Core.Routing;
using Server.Core.Services;

namespace Server.Api;

public static class ApiExtensions
{
  public static IServiceCollection AddApiServices(this IServiceCollection services)
  {
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

    var dispatcher = new ApiDispatcher();
    ApiRoutes.Register(dispatcher);
    services.AddSingleton(dispatcher);

    return services;
  }

  public static WebApplication UseApiMiddleware(this WebApplication app)
  {
    app.UseMiddleware<CancellationMiddleware>();
    app.UseMiddleware<AuthMiddleware>();
    app.UseMiddleware<AuthzMiddleware>();
    return app;
  }

  public static WebApplication MapApiEndpoints(this WebApplication app)
  {
    SnapshotEndpoint.Map(app);
    StreamEndpoints.Map(app);
    DispatchEndpoint.Map(app);
    return app;
  }
}
