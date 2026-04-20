using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Client.Core;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Client.Desktop.Services;
using Client.Desktop.ViewModels;
using Client.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

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
        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Services.GetRequiredService<ClientLifecycleService>().ShutdownAsync(shutdownCts.Token);
      };

      _ = AutoConnectAsync();
    }

    base.OnFrameworkInitializationCompleted();
  }

  private async Task AutoConnectAsync()
  {
    var settings = Services.GetRequiredService<DesktopSettings>();

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow mw })
      mw.MinimizeOnClose = settings.MinimizeOnClose;

    var options = new ConnectionOptions(
      settings.LastSuccessfulAddressIndex,
      settings.ReprobeEnabled);

    var lifecycle = Services.GetRequiredService<ClientLifecycleService>();
    var outcome = await lifecycle.AutoConnectAsync(options, settings.NotificationRules, CancellationToken.None);

    if (outcome.Status == AutoConnectStatus.Connected)
    {
      settings.LastSuccessfulAddressIndex = outcome.ConnectedAddressIndex;
      _ = settings.SaveAsync();
    }
  }
}
