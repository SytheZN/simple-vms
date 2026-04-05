using Client.Core.Api;
using Client.Core.Events;
using Client.Core.Streaming;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Core;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddClientCore(this IServiceCollection services)
  {
    services.AddSingleton<ITransportFactory, TlsTransportFactory>();
    services.AddSingleton<ITunnelService, TunnelService>();
    services.AddSingleton<IApiClient, ApiClient>();
    services.AddSingleton<ILiveStreamService, LiveStreamService>();
    services.AddSingleton<IPlaybackService, PlaybackService>();
    services.AddSingleton<IEventService, EventService>();
    services.AddSingleton<NotificationRouter>();
    services.AddHttpClient();
    services.AddTransient<IEnrollmentClient, EnrollmentClient>();
    services.AddTransient<EnrollmentViewModel>();
    services.AddTransient<GalleryViewModel>();
    services.AddTransient<CameraViewModel>();
    services.AddTransient<TimelineViewModel>();
    services.AddTransient<EventsViewModel>();
    services.AddTransient<SettingsViewModel>();
    return services;
  }
}
