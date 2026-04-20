using System.Diagnostics.CodeAnalysis;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace Client.Android.Services;

[Service(
  Exported = false,
  ForegroundServiceType = ForegroundService.TypeDataSync)]
[ExcludeFromCodeCoverage]
public sealed class TunnelForegroundService : Service
{
  private const int NotificationId = 1001;
  private const string ChannelId = "tunnel";

  public static void Start(global::Android.Content.Context context)
  {
    var intent = new Intent(context, typeof(TunnelForegroundService));
    ContextCompat.StartForegroundService(context, intent);
  }

  public static void Stop(global::Android.Content.Context context)
  {
    var intent = new Intent(context, typeof(TunnelForegroundService));
    context.StopService(intent);
  }

  public override IBinder? OnBind(Intent? intent) => null;

  public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
  {
    EnsureChannel();

    var pending = PendingIntent.GetActivity(
      this,
      0,
      new Intent(this, typeof(MainActivity)),
      PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

    var builder = new NotificationCompat.Builder(this, ChannelId);
    builder.SetContentTitle("SimpleVMS");
    builder.SetContentText("Tunnel active");
    builder.SetSmallIcon(Resource.Drawable.ic_notification);
    builder.SetOngoing(true);
    builder.SetContentIntent(pending);
    var notification = builder.Build()!;

    StartForeground(NotificationId, notification);
    return StartCommandResult.NotSticky;
  }

  private void EnsureChannel()
  {
    var channelBuilder = new NotificationChannelCompat.Builder(ChannelId, NotificationManagerCompat.ImportanceLow);
    channelBuilder.SetName("Tunnel");
    channelBuilder.SetDescription("Keeps the server connection alive");
    NotificationManagerCompat.From(this)!.CreateNotificationChannel(channelBuilder.Build()!);
  }
}
