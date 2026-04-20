using System.Diagnostics.CodeAnalysis;
using Android.App;
using Android.Content;
using Android.Util;

namespace Client.Android.Services;

[BroadcastReceiver(Exported = true, Enabled = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
[ExcludeFromCodeCoverage]
public sealed class BootReceiver : BroadcastReceiver
{
  public override void OnReceive(Context? context, Intent? intent)
  {
    if (context == null) return;
    var settings = new AndroidSettings(context.ApplicationContext ?? context);
    if (!settings.StartOnBoot) return;
    try
    {
      TunnelForegroundService.Start(context);
    }
    catch (Java.Lang.Exception ex)
    {
      Log.Warn("BootReceiver", $"Tunnel foreground service start failed: {ex.Message}");
    }
  }
}
