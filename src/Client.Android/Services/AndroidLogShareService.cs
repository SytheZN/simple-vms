using System.Diagnostics.CodeAnalysis;
using Android.Content;
using AndroidX.Core.Content;
using Client.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Client.Android.Services;

[ExcludeFromCodeCoverage]
public sealed class AndroidLogShareService
{
  private readonly global::Android.Content.Context _context;
  private readonly DiagnosticsInfo _diagnostics;
  private readonly ILogger<AndroidLogShareService> _logger;

  public AndroidLogShareService(
    global::Android.Content.Context context,
    DiagnosticsInfo diagnostics,
    ILogger<AndroidLogShareService> logger)
  {
    _context = context;
    _diagnostics = diagnostics;
    _logger = logger;
  }

  public void Share()
  {
    var path = _diagnostics.LogFilePath;
    if (path == null) return;

    try
    {
      var file = new Java.IO.File(path);
      if (!file.Exists())
      {
        _logger.LogWarning("Log file {LogPath} does not exist", path);
        return;
      }

      var uri = FileProvider.GetUriForFile(_context, $"{_context.PackageName}.fileprovider", file);

      var send = new Intent(Intent.ActionSend);
      send.SetType("text/plain");
      send.PutExtra(Intent.ExtraStream, uri);
      send.PutExtra(Intent.ExtraSubject, "SimpleVMS log");
      send.AddFlags(ActivityFlags.GrantReadUriPermission);

      var chooser = Intent.CreateChooser(send, "Share log file");
      chooser!.AddFlags(ActivityFlags.NewTask);
      _context.StartActivity(chooser);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Sharing log file {LogPath} failed", path);
    }
  }
}
