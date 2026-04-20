using Avalonia;
using Client.Android.Services;
using Client.Android.ViewModels;
using Client.Core;
using Client.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Client.Android;

public static class Program
{
  private static readonly object _gate = new();
  public static IServiceProvider Services { get; private set; } = null!;

  public static IServiceProvider BuildServices(global::Android.Content.Context context)
  {
    lock (_gate)
    {
      if (Services != null) return Services;
      return BuildInner(context);
    }
  }

  private static IServiceProvider BuildInner(global::Android.Content.Context context)
  {

    var services = new ServiceCollection();
    services.AddClientCore();

    var logDir = context.GetExternalFilesDir(null)?.AbsolutePath ?? context.FilesDir!.AbsolutePath;
    var logPath = Path.Combine(logDir, "client.log");
    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Debug()
      .WriteTo.File(logPath)
      .CreateLogger();

    services.AddLogging(b =>
    {
      b.SetMinimumLevel(LogLevel.Debug);
      b.AddSerilog();
    });

    services.AddSingleton(new DiagnosticsInfo(logPath));
    services.AddSingleton<ICredentialStore>(sp =>
      new AndroidCredentialStore(context, sp.GetRequiredService<ILogger<AndroidCredentialStore>>()));
    services.AddSingleton<INotificationService>(_ => new AndroidNotificationService(context));
    services.AddSingleton(_ => new AndroidSettings(context));
    services.AddSingleton<IQrScannerService>(_ => new AndroidQrScannerService(context));
    services.AddSingleton<MainShellViewModel>();

    Services = services.BuildServiceProvider();
    return Services;
  }

  public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<AndroidApp>();
}
