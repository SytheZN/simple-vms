using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Client.Android;

[Application]
public sealed class SimpleVmsAndroidApplication : AvaloniaAndroidApplication<AndroidApp>
{
  public SimpleVmsAndroidApplication(IntPtr handle, JniHandleOwnership ownership)
    : base(handle, ownership) { }

  protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
  {
    var services = Program.BuildServices(this);
    return base.CustomizeAppBuilder(builder)
      .AfterSetup(_ =>
      {
        if (Avalonia.Application.Current is AndroidApp app)
        {
          app.Services = services;
          app.AndroidContext = this;
        }
      });
  }
}
