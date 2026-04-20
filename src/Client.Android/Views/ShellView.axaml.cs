using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Client.Android.ViewModels;
using Client.Core.Tunnel;
using System.ComponentModel;

namespace Client.Android.Views;

public sealed partial class ShellView : UserControl
{
  private const double WideLayoutBreakpointDip = 600;

  private Ellipse? _statusDot;
  private MainShellViewModel? _subscribed;

  public ShellView()
  {
    InitializeComponent();
    _statusDot = this.FindControl<Ellipse>("StatusDot");
    PropertyChanged += OnPropertyChanged;
    DataContextChanged += OnDataContextChanged;
    DetachedFromVisualTree += (_, _) => Unsubscribe();
  }

  private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

  private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
  {
    if (e.Property != BoundsProperty) return;
    if (DataContext is not MainShellViewModel vm) return;
    vm.IsWideLayout = Bounds.Width >= WideLayoutBreakpointDip;
  }

  private void OnDataContextChanged(object? sender, EventArgs e)
  {
    Unsubscribe();
    if (DataContext is not MainShellViewModel vm) return;
    _subscribed = vm;
    UpdateStatusDot(vm.ConnectionState);
    vm.PropertyChanged += OnVmPropertyChanged;
  }

  private void Unsubscribe()
  {
    if (_subscribed != null) _subscribed.PropertyChanged -= OnVmPropertyChanged;
    _subscribed = null;
  }

  private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName != nameof(MainShellViewModel.ConnectionState)) return;
    if (sender is MainShellViewModel vm) UpdateStatusDot(vm.ConnectionState);
  }

  private void UpdateStatusDot(ConnectionState state)
  {
    if (_statusDot == null) return;
    _statusDot.Classes.Remove("status-connected");
    _statusDot.Classes.Remove("status-connecting");
    _statusDot.Classes.Remove("status-disconnected");
    _statusDot.Classes.Add(state switch
    {
      ConnectionState.Connected => "status-connected",
      ConnectionState.Connecting => "status-connecting",
      _ => "status-disconnected"
    });
  }
}
