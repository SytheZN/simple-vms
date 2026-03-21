using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Cameras.Onvif;

public sealed partial class OnvifProvider : IPlugin
{
  private IConfig _config = null!;
  private HttpClient _http = null!;
  private SoapClient _soap = null!;
  private DeviceService _device = null!;
  private MediaService _media = null!;
  private EventService _events = null!;

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
    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    _soap = new SoapClient(_http);
    _device = new DeviceService(_soap);
    _media = new MediaService(_soap);
    _events = new EventService(_soap);
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
