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
    if (ApplicationLifetime is ISingleViewApplicationLifetime)
    {
      ResetMainView();
      _ = AutoConnectAsync();
    }
    base.OnFrameworkInitializationCompleted();
  }

  internal void ResetMainView()
  {
    if (ApplicationLifetime is not ISingleViewApplicationLifetime single) return;

    var shellVm = Services.GetRequiredService<MainShellViewModel>();
    single.MainView = new ShellView { DataContext = shellVm };
  }

  private readonly SemaphoreSlim _suspendGate = new(1, 1);
  private bool _suspended;

  internal bool IsSuspended => _suspended;

  internal async Task ReconnectAsync()
  {
    await _suspendGate.WaitAsync();
    try
    {
      if (!_suspended) return;
      _suspended = false;
      await AutoConnectAsync();
    }
    finally { _suspendGate.Release(); }
  }

  internal async Task SuspendAsync()
  {
    await _suspendGate.WaitAsync();
    try
    {
      if (_suspended) return;
      _suspended = true;

      if (AndroidContext != null)
        TunnelForegroundService.Stop(AndroidContext);

      await Services.GetRequiredService<ITunnelService>().DisconnectAsync();
    }
    catch (Exception ex)
    {
      Services.GetRequiredService<ILogger<AndroidApp>>()
        .LogError(ex, "Suspending tunnel on background failed");
    }
    finally { _suspendGate.Release(); }
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
