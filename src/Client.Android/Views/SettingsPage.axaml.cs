using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ToggleButton = Avalonia.Controls.Primitives.ToggleButton;
using Button = Avalonia.Controls.Button;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Client.Android.Services;
using Client.Android.ViewModels;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Client.Android.Views;

public sealed partial class SettingsPage : UserControl
{
  private Ellipse? _statusDot;
  private TextBlock? _statusLabel;
  private ToggleButton? _startOnBootToggle;
  private SettingsViewModel? _subscribed;

  public SettingsPage()
  {
    InitializeComponent();

    _statusDot = this.FindControl<Ellipse>("StatusDot");
    _statusLabel = this.FindControl<TextBlock>("StatusLabel");
    _startOnBootToggle = this.FindControl<ToggleButton>("StartOnBootToggle");

    this.FindControl<Button>("DisconnectButton")!.Click += OnDisconnect;

    if (_startOnBootToggle != null)
      _startOnBootToggle.IsCheckedChanged += OnStartOnBootChanged;

    DataContextChanged += (_, _) => Rebind();
    DetachedFromVisualTree += (_, _) => Unsubscribe();
  }

  private void Rebind()
  {
    Unsubscribe();
    if (DataContext is SettingsViewModel vm)
    {
      _subscribed = vm;
      _ = vm.LoadAsync();
      vm.PropertyChanged += OnVmPropertyChanged;
      UpdateConnectionStatus(vm.ConnectionState);
    }
    if (_startOnBootToggle != null)
      _startOnBootToggle.IsChecked = GetAndroidSettings()?.StartOnBoot ?? false;
  }

  private void Unsubscribe()
  {
    if (_subscribed != null) _subscribed.PropertyChanged -= OnVmPropertyChanged;
    _subscribed = null;
  }

  private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

  private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(SettingsViewModel.ConnectionState) && DataContext is SettingsViewModel vm)
      UpdateConnectionStatus(vm.ConnectionState);
  }

  private void UpdateConnectionStatus(ConnectionState state)
  {
    if (_statusDot == null || _statusLabel == null) return;
    _statusDot.Classes.Clear();
    _statusDot.Classes.Add("status-dot");
    _statusDot.Classes.Add(state switch
    {
      ConnectionState.Connected => "status-connected",
      ConnectionState.Connecting => "status-connecting",
      _ => "status-disconnected"
    });
    _statusLabel.Text = state.ToString();
  }

  private void OnStartOnBootChanged(object? sender, RoutedEventArgs e)
  {
    var settings = GetAndroidSettings();
    if (settings == null || _startOnBootToggle == null) return;
    settings.StartOnBoot = _startOnBootToggle.IsChecked == true;
  }

  private void OnDisconnect(object? sender, RoutedEventArgs e)
  {
    if (DataContext is not SettingsViewModel vm) return;
    _ = Disconnect(vm);
  }

  private static async Task Disconnect(SettingsViewModel vm)
  {
    await vm.DisconnectAsync();
    if (Avalonia.Application.Current is AndroidApp app)
    {
      if (app.AndroidContext != null)
        TunnelForegroundService.Stop(app.AndroidContext);
      app.Services.GetRequiredService<MainShellViewModel>()
        .NavigateTo(MainShellViewModel.ViewKind.Enrollment);
    }
  }

  private AndroidSettings? GetAndroidSettings() =>
    (Avalonia.Application.Current as AndroidApp)?.Services.GetService<AndroidSettings>();
}
