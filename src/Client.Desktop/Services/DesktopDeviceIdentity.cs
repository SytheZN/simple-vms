using Client.Core.Platform;

namespace Client.Desktop.Services;

public sealed class DesktopDeviceIdentity : IDeviceIdentity
{
  public string DeviceName { get; } = Environment.MachineName;
}
