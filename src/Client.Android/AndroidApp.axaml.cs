using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Client.Android.Services;
using Client.Android.ViewModels;
using Client.Android.Views;
using Client.Core;
using Client.Core.Tunnel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Client.Android;

public sealed class AndroidApp : Avalonia.Application
{
  public IServiceProvider Services { get; set; } = null!;
  public global::Android.Content.Context? AndroidContext { get; set; }

  public override void Initialize() => AvaloniaXamlLoader.Load(this);

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is ISingleViewApplicationLifetime single)
    {
      var shellVm = Services.GetRequiredService<MainShellViewModel>();
      single.MainView = new ShellView { DataContext = shellVm };
      _ = AutoConnectAsync();
    }
    base.OnFrameworkInitializationCompleted();
  }

  private async Task AutoConnectAsync()
  {
    var logger = Services.GetRequiredService<ILogger<AndroidApp>>();
    try
    {
      var lifecycle = Services.GetRequiredService<ClientLifecycleService>();
      var outcome = await lifecycle.AutoConnectAsync(new ConnectionOptions(), [], CancellationToken.None);
      if (outcome.Status == AutoConnectStatus.Connected && AndroidContext != null)
        TunnelForegroundService.Start(AndroidContext);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "AutoConnect on launch failed");
    }
  }
}
