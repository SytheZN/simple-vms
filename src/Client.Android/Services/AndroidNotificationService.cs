using System.Diagnostics.CodeAnalysis;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Client.Core.Platform;

namespace Client.Android.Services;

[ExcludeFromCodeCoverage]
public sealed class AndroidNotificationService : INotificationService
{
  private const string ChannelId = "cameras";
  private const string ChannelName = "Camera events";

  private readonly global::Android.Content.Context _context;
  private int _nextId;

  public AndroidNotificationService(global::Android.Content.Context context)
  {
    _context = context;
    EnsureChannel();
  }

  public Task ShowAsync(string title, string body, Guid? cameraId = null)
  {
    var intent = new Intent(_context, typeof(MainActivity));
    intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
    if (cameraId != null)
      intent.PutExtra("cameraId", cameraId.Value.ToString());

    var pending = PendingIntent.GetActivity(
      _context,
      0,
      intent,
      PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

    var builder = new NotificationCompat.Builder(_context, ChannelId);
    builder.SetContentTitle(title);
    builder.SetContentText(body);
    builder.SetSmallIcon(Resource.Drawable.ic_notification);
    builder.SetAutoCancel(true);
    builder.SetContentIntent(pending);
    var notification = builder.Build()!;

    var manager = NotificationManagerCompat.From(_context)!;
    if (manager.AreNotificationsEnabled())
    {
      var id = cameraId is { } cid
        ? cid.GetHashCode()
        : Interlocked.Increment(ref _nextId);
      manager.Notify(id, notification);
    }

    return Task.CompletedTask;
  }

  private void EnsureChannel()
  {
    var channelBuilder = new NotificationChannelCompat.Builder(ChannelId, NotificationManagerCompat.ImportanceDefault);
    channelBuilder.SetName(ChannelName);
    channelBuilder.SetDescription("Camera event alerts");
    NotificationManagerCompat.From(_context)!.CreateNotificationChannel(channelBuilder.Build()!);
  }
}
