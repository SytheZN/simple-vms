using Client.Core.Platform;

namespace Tests.Unit.Client.Mocks;

public sealed class FakeDeviceIdentity : IDeviceIdentity
{
  public string DeviceName { get; init; } = "TestDevice";
}
