using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Client.Core.Tunnel;
using Client.Desktop.ViewModels;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Client.Desktop.Views;

[ExcludeFromCodeCoverage]
public partial class MainWindow : Window
{
  private readonly Ellipse _statusDot;
  private readonly TextBlock _statusLabel;

  public MainWindow()
  {
    InitializeComponent();
    _statusDot = this.FindControl<Ellipse>("StatusDot")!;
    _statusLabel = this.FindControl<TextBlock>("StatusLabel")!;
    if (OperatingSystem.IsMacOS()) RemapKeyBindingsForMac();
  }

  private void RemapKeyBindingsForMac()
  {
    foreach (var kb in KeyBindings)
    {
      kb.Gesture = kb.Gesture.Key switch
      {
        Key.F11 => new KeyGesture(Key.F, KeyModifiers.Meta),
        Key.Left when kb.Gesture.KeyModifiers == KeyModifiers.Control
          => new KeyGesture(Key.Left, KeyModifiers.Meta),
        Key.Right when kb.Gesture.KeyModifiers == KeyModifiers.Control
          => new KeyGesture(Key.Right, KeyModifiers.Meta),
        Key.D when kb.Gesture.KeyModifiers == KeyModifiers.Control
          => new KeyGesture(Key.D, KeyModifiers.Meta),
        _ => kb.Gesture
      };
    }
  }

  protected override void OnDataContextChanged(EventArgs e)
  {
    base.OnDataContextChanged(e);
    if (DataContext is MainWindowViewModel vm)
    {
      vm.PropertyChanged += OnViewModelPropertyChanged;
      UpdateStatusDot(vm.ConnectionState);
    }
  }

  private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (sender is not MainWindowViewModel vm) return;

    if (e.PropertyName == nameof(MainWindowViewModel.IsFullscreen))
      WindowState = vm.IsFullscreen ? WindowState.FullScreen : WindowState.Normal;
    else if (e.PropertyName == nameof(MainWindowViewModel.ConnectionState))
      UpdateStatusDot(vm.ConnectionState);
  }

  private void UpdateStatusDot(ConnectionState state)
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

  public bool MinimizeOnClose { get; set; }

  protected override void OnClosing(WindowClosingEventArgs e)
  {
    if (MinimizeOnClose)
    {
      e.Cancel = true;
      Hide();
      return;
    }
    base.OnClosing(e);
  }

  public void BringToFront()
  {
    Show();
    WindowState = WindowState.Normal;
    Activate();
  }
}
