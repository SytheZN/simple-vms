using Avalonia.Controls;
using Client.Core.ViewModels;
using Client.Desktop.ViewModels;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Client.Desktop.Views;

[ExcludeFromCodeCoverage]
public partial class EnrollmentView : UserControl
{
  public EnrollmentView()
  {
    InitializeComponent();
    DataContextChanged += OnDataContextChanged;
  }

  private void OnDataContextChanged(object? sender, EventArgs e)
  {
    if (DataContext is EnrollmentViewModel vm)
      vm.PropertyChanged += OnVmPropertyChanged;
  }

  private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(EnrollmentViewModel.IsEnrolled) &&
        sender is EnrollmentViewModel { IsEnrolled: true })
    {
      var main = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
      main?.NavigateTo(MainWindowViewModel.ViewKind.Gallery);
    }
  }
}
