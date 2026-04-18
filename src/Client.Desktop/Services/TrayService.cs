using Avalonia.Controls;
using Avalonia.Threading;
using Client.Core.Tunnel;
using Client.Desktop.Views;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Client.Desktop.Services;

[ExcludeFromCodeCoverage]
public sealed class TrayService : IDisposable
{
  private readonly ITunnelService _tunnel;
  private readonly ILogger<TrayService> _logger;
  private TrayIcon? _trayIcon;
  private MainWindow? _window;

  public TrayService(ITunnelService tunnel, ILogger<TrayService> logger)
  {
    _tunnel = tunnel;
    _logger = logger;
    _tunnel.StateChanged += OnStateChanged;
  }

  public void Initialize(MainWindow window)
  {
    _window = window;

    try
    {
      var showItem = new NativeMenuItem("Show");
      showItem.Click += (_, _) => Dispatcher.UIThread.Post(() => _window.BringToFront());

      var quitItem = new NativeMenuItem("Quit");
      quitItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
      {
        _window.MinimizeOnClose = false;
        _window.Close();
      });

      var menu = new NativeMenu { showItem, new NativeMenuItemSeparator(), quitItem };

      var uri = new Uri("avares://Client.Core/Assets/logo/32.png");
      using var iconStream = Avalonia.Platform.AssetLoader.Open(uri);

      _trayIcon = new TrayIcon
      {
        ToolTipText = GetTooltip(_tunnel.State),
        Menu = menu,
        IsVisible = true,
        Icon = new WindowIcon(iconStream)
      };
      _trayIcon.Clicked += (_, _) => Dispatcher.UIThread.Post(() => _window.BringToFront());
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "System tray not available");
    }
  }

  private void OnStateChanged(ConnectionState state)
  {
    Dispatcher.UIThread.Post(() =>
    {
      if (_trayIcon != null)
        _trayIcon.ToolTipText = GetTooltip(state);
    });
  }

  private static string GetTooltip(ConnectionState state) => state switch
  {
    ConnectionState.Connected => "VMS - Connected",
    ConnectionState.Connecting => "VMS - Connecting...",
    _ => "VMS - Disconnected"
  };

  public void Dispose()
  {
    _tunnel.StateChanged -= OnStateChanged;
    _trayIcon?.Dispose();
  }
}
