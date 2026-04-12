using Avalonia.Controls;
using Client.Core.Controls;
using Client.Core.ViewModels;
using Client.Desktop.Services;
using Client.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Client.Desktop.Views;

[ExcludeFromCodeCoverage]
public partial class GalleryView : UserControl
{
  private readonly StackPanel _emptyState;

  public GalleryView()
  {
    InitializeComponent();

    var grid = this.FindControl<CameraGrid>("Grid")!;
    var dec = this.FindControl<Button>("DecColumns")!;
    var inc = this.FindControl<Button>("IncColumns")!;
    var label = this.FindControl<TextBlock>("ColumnLabel")!;
    _emptyState = this.FindControl<StackPanel>("EmptyState")!;

    dec.Click += (_, _) =>
    {
      if (DataContext is GalleryViewModel vm && vm.Columns > 1)
      {
        vm.Columns--;
        label.Text = $"{vm.Columns} cols";
        SaveColumns(vm.Columns);
      }
    };

    inc.Click += (_, _) =>
    {
      if (DataContext is GalleryViewModel vm && vm.Columns < 8)
      {
        vm.Columns++;
        label.Text = $"{vm.Columns} cols";
        SaveColumns(vm.Columns);
      }
    };

    grid.ItemClicked += index =>
    {
      if (DataContext is GalleryViewModel vm && index < vm.Cameras.Count)
        FindMainWindowViewModel()?.NavigateToCamera(vm.Cameras[index].Id);
    };

    DataContextChanged += (_, _) =>
    {
      if (DataContext is GalleryViewModel vm)
      {
        var settings = ((App)Avalonia.Application.Current!).Services.GetService<DesktopSettings>();
        if (settings != null)
          vm.Columns = settings.GalleryColumns;
        label.Text = $"{vm.Columns} cols";
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.Cameras.CollectionChanged += OnCamerasChanged;
        vm.CameraEventReceived += cameraId => OnCameraEvent(grid, vm, cameraId);
        _ = vm.LoadAsync(CancellationToken.None);
      }
    };
  }

  private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(GalleryViewModel.IsLoading))
      UpdateEmptyState();
  }

  private void OnCamerasChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
    UpdateEmptyState();

  private void UpdateEmptyState()
  {
    if (DataContext is GalleryViewModel vm)
      _emptyState.IsVisible = !vm.IsLoading && vm.Cameras.Count == 0;
  }

  private static void OnCameraEvent(CameraGrid grid, GalleryViewModel vm, Guid cameraId)
  {
    for (var i = 0; i < vm.Cameras.Count; i++)
    {
      if (vm.Cameras[i].Id == cameraId)
      {
        grid.FlashCamera(i);
        return;
      }
    }
  }

  private void SaveColumns(int columns)
  {
    var settings = ((App)Avalonia.Application.Current!).Services.GetService<DesktopSettings>();
    if (settings == null) return;
    settings.GalleryColumns = columns;
    _ = settings.SaveAsync();
  }

  private MainWindowViewModel? FindMainWindowViewModel() =>
    TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
}
