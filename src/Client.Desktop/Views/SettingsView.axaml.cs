using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Client.Desktop.Services;
using Client.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Client.Desktop.Views;

[ExcludeFromCodeCoverage]
public partial class SettingsView : UserControl
{
  private static (string Keys, string Description)[] Shortcuts =>
    OperatingSystem.IsMacOS()
      ?
      [
        ("\u2318 F", "Toggle fullscreen"),
        ("\u2318 \u2192", "Next camera"),
        ("\u2318 \u2190", "Previous camera"),
        ("Space", "Play / pause"),
        ("Esc", "Back / exit fullscreen"),
        ("1", "Gallery"),
        ("2", "Camera"),
        ("3", "Settings"),
        ("\u2318 D", "Toggle stats overlay"),
      ]
      :
      [
        ("F11", "Toggle fullscreen"),
        ("Ctrl + \u2192", "Next camera"),
        ("Ctrl + \u2190", "Previous camera"),
        ("Space", "Play / pause"),
        ("Esc", "Back / exit fullscreen"),
        ("1", "Gallery"),
        ("2", "Camera"),
        ("3", "Settings"),
        ("Ctrl + D", "Toggle stats overlay"),
      ];

  private readonly Ellipse _statusDot;
  private readonly TextBlock _statusLabel;

  public SettingsView()
  {
    InitializeComponent();

    _statusDot = this.FindControl<Ellipse>("StatusDot")!;
    _statusLabel = this.FindControl<TextBlock>("StatusLabel")!;

    var reprobeToggle = this.FindControl<ToggleButton>("ReprobeToggle")!;
    var minimizeToggle = this.FindControl<ToggleButton>("MinimizeOnCloseToggle")!;

    this.FindControl<Button>("DisconnectButton")!.Click += OnDisconnect;

    reprobeToggle.IsCheckedChanged += (_, _) =>
    {
      var settings = GetSettings();
      if (settings == null) return;
      settings.ReprobeEnabled = reprobeToggle.IsChecked == true;
      _ = settings.SaveAsync();
    };

    minimizeToggle.IsCheckedChanged += (_, _) =>
    {
      var settings = GetSettings();
      if (settings == null) return;
      settings.MinimizeOnClose = minimizeToggle.IsChecked == true;
      _ = settings.SaveAsync();
      if (TopLevel.GetTopLevel(this) is MainWindow mw)
        mw.MinimizeOnClose = settings.MinimizeOnClose;
    };

    BuildShortcutsCard();

    DataContextChanged += (_, _) =>
    {
      if (DataContext is SettingsViewModel vm)
      {
        _ = vm.LoadAsync();
        vm.PropertyChanged += OnVmPropertyChanged;
        UpdateConnectionStatus(vm.ConnectionState);

        var settings = GetSettings();
        if (settings != null)
        {
          reprobeToggle.IsChecked = settings.ReprobeEnabled;
          minimizeToggle.IsChecked = settings.MinimizeOnClose;
        }
      }
    };
  }

  private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(SettingsViewModel.ConnectionState) && DataContext is SettingsViewModel vm)
      UpdateConnectionStatus(vm.ConnectionState);
  }

  private void UpdateConnectionStatus(ConnectionState state)
  {
    _statusDot.Classes.Clear();
    _statusDot.Classes.Add("status-dot");
    _statusDot.Classes.Add(state switch
    {
      ConnectionState.Connected => "status-connected",
      ConnectionState.Connecting => "status-connecting",
      _ => "status-disconnected"
    });
    _statusLabel.Text = state switch
    {
      ConnectionState.Connected => "Connected",
      ConnectionState.Connecting => "Connecting...",
      _ => "Disconnected"
    };
  }

  private async void OnDisconnect(object? sender, RoutedEventArgs e)
  {
    if (DataContext is not SettingsViewModel vm) return;
    await vm.DisconnectAsync();
    var main = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
    main?.NavigateTo(MainWindowViewModel.ViewKind.Enrollment);
  }

  private static DesktopSettings? GetSettings() =>
    ((App)Avalonia.Application.Current!).Services.GetService<DesktopSettings>();

  private void BuildShortcutsCard()
  {
    var list = this.FindControl<StackPanel>("ShortcutsList")!;
    foreach (var (keys, description) in Shortcuts)
    {
      var row = new DockPanel { Margin = new Avalonia.Thickness(0, 2) };

      var keyPanel = new StackPanel
      {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        [DockPanel.DockProperty] = Dock.Right,
        HorizontalAlignment = HorizontalAlignment.Right
      };

      foreach (var part in keys.Split(" + "))
      {
        var glyph = new Border();
        glyph.Classes.Add("key-glyph");
        var label = new TextBlock { Text = part };
        glyph.Child = label;
        keyPanel.Children.Add(glyph);
      }

      var desc = new TextBlock
      {
        Text = description,
        VerticalAlignment = VerticalAlignment.Center
      };
      desc.Classes.Add("text-muted");

      row.Children.Add(keyPanel);
      row.Children.Add(desc);
      list.Children.Add(row);
    }
  }
}
