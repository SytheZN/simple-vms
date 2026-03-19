using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Cameras.Onvif;

public sealed partial class OnvifProvider : IPlugin, IPluginSettings
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

  public IReadOnlyList<SettingGroup> GetSchema() =>
  [
    new SettingGroup
    {
      Key = "credentials",
      Order = 0,
      Label = "Default Credentials",
      Description = "Default credentials used for camera discovery",
      Fields =
      [
        new SettingField
        {
          Key = "username",
          Order = 0,
          Label = "Username",
          Type = "string",
          DefaultValue = "admin",
          Required = false
        },
        new SettingField
        {
          Key = "password",
          Order = 1,
          Label = "Password",
          Type = "password",
          Required = false
        }
      ]
    }
  ];

  public IReadOnlyDictionary<string, object> GetValues() =>
    new Dictionary<string, object>
    {
      ["username"] = _config.Get("username", "admin"),
      ["password"] = _config.Get("password", "")
    };

  public OneOf<Success, Error> ValidateValue(string key, object value)
  {
    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, object> values)
  {
    if (values.TryGetValue("username", out var user))
      _config.Set("username", user.ToString()!);
    if (values.TryGetValue("password", out var pass))
      _config.Set("password", pass.ToString()!);
    return new Success();
  }
}
