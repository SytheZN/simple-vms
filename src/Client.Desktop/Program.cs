using Avalonia;
using Client.Core;
using Client.Core.Platform;
using Client.Desktop.Services;
using Client.Desktop.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Client.Desktop;

public static class Program
{
  [STAThread]
  public static async Task Main(string[] args)
  {
    var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
    var configuration = new ConfigurationBuilder()
      .SetBasePath(AppContext.BaseDirectory)
      .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
      .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
      .Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddClientCore();
    var logDir = DesktopSettings.ConfigDir;
    Directory.CreateDirectory(logDir);
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
    services.AddSingleton<ICredentialStore>(CredentialStoreFactory.Create());
    services.AddSingleton<INotificationService, DesktopNotificationService>();
    services.AddSingleton<DesktopSettings>();
    services.AddSingleton<TrayService>();
    services.AddSingleton<MainWindowViewModel>();

    var provider = services.BuildServiceProvider();
    var logger = provider.GetRequiredService<ILogger<App>>();
    logger.LogInformation("Application starting");

    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
      logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
      Log.CloseAndFlush();
      Environment.Exit(1);
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
      e.SetObserved();
      logger.LogCritical(e.Exception, "Unobserved task exception");
      Log.CloseAndFlush();
      Environment.Exit(1);
    };

    var crashed = false;
    try
    {
      var app = BuildAvaloniaApp();
      app.AfterSetup(_ =>
      {
        if (Application.Current is App a)
          a.Services = provider;
      });
      app.StartWithClassicDesktopLifetime(args);
    }
    catch (Exception ex)
    {
      crashed = true;
      logger.LogCritical(ex, "Application crashed");
    }

    await Log.CloseAndFlushAsync();
    if (crashed) Environment.Exit(1);
  }

  public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .LogToTrace();
}
