using Client.Core.Platform;

namespace Client.Android.Services;

public sealed class AndroidDeviceIdentity : IDeviceIdentity
{
  public string DeviceName { get; }

  public AndroidDeviceIdentity(global::Android.Content.Context context)
  {
    var userSet = global::Android.Provider.Settings.Global.GetString(
      context.ContentResolver, "device_name");
    DeviceName = string.IsNullOrWhiteSpace(userSet)
      ? global::Android.OS.Build.Model ?? "Android"
      : userSet;
  }
}
