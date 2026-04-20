using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Client.Android.Services;
using Client.Android.ViewModels;
using Client.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Client.Android.Views;

public sealed partial class EnrollmentPage : UserControl
{
  private EnrollmentViewModel? _subscribed;

  public EnrollmentPage()
  {
    InitializeComponent();
    DataContextChanged += (_, _) => Rebind();
    DetachedFromVisualTree += (_, _) => Unsubscribe();
  }

  private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

  private void Rebind()
  {
    Unsubscribe();
    if (DataContext is EnrollmentViewModel vm)
    {
      _subscribed = vm;
      vm.PropertyChanged += OnVmPropertyChanged;
    }
  }

  private void Unsubscribe()
  {
    if (_subscribed != null) _subscribed.PropertyChanged -= OnVmPropertyChanged;
    _subscribed = null;
  }

  private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName != nameof(EnrollmentViewModel.IsEnrolled)) return;
    if (sender is not EnrollmentViewModel { IsEnrolled: true }) return;
    if (Avalonia.Application.Current is not AndroidApp app) return;

    if (app.AndroidContext != null)
      TunnelForegroundService.Start(app.AndroidContext);
    app.Services.GetRequiredService<MainShellViewModel>()
      .NavigateTo(MainShellViewModel.ViewKind.Gallery);
  }
}
