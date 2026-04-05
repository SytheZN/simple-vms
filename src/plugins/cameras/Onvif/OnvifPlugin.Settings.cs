using Shared.Models;

namespace Cameras.Onvif;

public sealed partial class OnvifProvider : IPluginSettings
{
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

  public IReadOnlyDictionary<string, string> GetValues() =>
    new Dictionary<string, string>
    {
      ["username"] = _config.Get("username", "admin"),
      ["password"] = _config.Get("password", "")
    };

  public OneOf<Success, Error> ValidateValue(string key, string value)
  {
    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, string> values)
  {
    if (values.TryGetValue("username", out var user))
      _config.Set("username", user);
    if (values.TryGetValue("password", out var pass))
      _config.Set("password", pass);
    return new Success();
  }
}
