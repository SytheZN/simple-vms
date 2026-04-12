using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Client.Core.Events;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Client.Desktop.Services;
using Client.Desktop.ViewModels;
using Client.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Client.Desktop;

public sealed class App : Application
{
  public IServiceProvider Services { get; set; } = null!;

  public override void Initialize() => AvaloniaXamlLoader.Load(this);

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      var settings = Services.GetRequiredService<DesktopSettings>();
      settings.Load();

      var vm = Services.GetRequiredService<MainWindowViewModel>();
      var window = new MainWindow { DataContext = vm };
      desktop.MainWindow = window;

      var tray = Services.GetRequiredService<TrayService>();
      tray.Initialize(window);

      var notifications = Services.GetRequiredService<INotificationService>();
      if (notifications is DesktopNotificationService dns)
        dns.SetWindow(window);

      desktop.ShutdownRequested += async (_, _) =>
      {
        var shutdownLogger = Services.GetRequiredService<ILogger<App>>();
        shutdownLogger.LogInformation("Shutdown starting");
        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = Services.GetRequiredService<IEventService>();
        await events.StopAsync(shutdownCts.Token);
        shutdownLogger.LogDebug("Event service stopped");
        var tunnel = Services.GetRequiredService<ITunnelService>();
        await tunnel.DisconnectAsync(shutdownCts.Token);
        shutdownLogger.LogDebug("Tunnel disconnected");
        shutdownLogger.LogInformation("Shutdown complete");
      };

      _ = AutoConnectAsync();
    }

    base.OnFrameworkInitializationCompleted();
  }

  private async Task AutoConnectAsync()
  {
    var logger = Services.GetRequiredService<ILogger<App>>();

    var credentials = Services.GetRequiredService<ICredentialStore>();
    var creds = await credentials.LoadAsync();
    if (creds == null)
    {
      logger.LogInformation("No stored credentials, skipping auto-connect");
      return;
    }

    logger.LogInformation("Credentials loaded for client {ClientId}, {AddressCount} address(es)",
      creds.ClientId, creds.Addresses.Length);

    var settings = Services.GetRequiredService<DesktopSettings>();

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow mw })
      mw.MinimizeOnClose = settings.MinimizeOnClose;

    var router = Services.GetRequiredService<NotificationRouter>();
    router.UpdateRules(settings.NotificationRules);

    var options = new ConnectionOptions(
      settings.LastSuccessfulAddressIndex,
      settings.ReprobeEnabled);

    logger.LogInformation("Connecting to tunnel (preferred index {Index}, reprobe {Reprobe})",
      options.LastSuccessfulIndex, options.ReprobeEnabled);
    var tunnel = Services.GetRequiredService<ITunnelService>();
    try { await tunnel.ConnectAsync(options, CancellationToken.None); }
    catch (Exception ex)
    {
      logger.LogError(ex, "Auto-connect failed");
      return;
    }

    settings.LastSuccessfulAddressIndex = tunnel.ConnectedAddressIndex;
    _ = settings.SaveAsync();

    logger.LogInformation("Tunnel connected at index {Index}, starting event service",
      tunnel.ConnectedAddressIndex);
    var events = Services.GetRequiredService<IEventService>();
    await events.StartAsync(CancellationToken.None);
  }
}
