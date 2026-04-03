using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Cameras.Onvif;

public sealed partial class OnvifProvider : IPlugin
{
  private IConfig _config = null!;
  private IEventBus? _eventBus;
  private HttpClient _http = null!;
  private SoapClient _soap = null!;
  private DeviceService _device = null!;
  private MediaService _media = null!;
  private EventService _events = null!;
  private AnalyticsService _analytics = null!;

  public PluginMetadata Metadata { get; } = new()
  {
    Id = "onvif",
    Name = "ONVIF Camera Provider",
    Version = "1.0.0",
    Description = "Camera discovery and configuration via ONVIF"
  };

  public OneOf<Success, Error> Initialize(PluginContext context)
  {
    _config = context.Config;
    _eventBus = context.EventBus;
    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };
    _soap = new SoapClient(_http, context.LoggerFactory.CreateLogger("Soap"));
    _device = new DeviceService(_soap);
    _media = new MediaService(_soap);
    _events = new EventService(_soap);
    _analytics = new AnalyticsService(_soap);
    return new Success();
  }

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct)
  {
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct)
  {
    _http.Dispose();
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }
}
