using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Client.Android.ViewModels;
using Client.Core.Controls;
using Client.Core.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Client.Android.Views;

public sealed partial class GalleryPage : UserControl
{
  private const double WideLayoutBreakpointDip = 600;

  private CameraGrid? _grid;
  private StackPanel? _emptyState;
  private GalleryViewModel? _subscribed;

  public GalleryPage()
  {
    InitializeComponent();

    _grid = this.FindControl<CameraGrid>("Grid");
    _emptyState = this.FindControl<StackPanel>("EmptyState");

    if (_grid != null)
    {
      _grid.ItemClicked += OnItemClicked;
    }

    DataContextChanged += (_, _) => Rebind();
    DetachedFromVisualTree += (_, _) => Unsubscribe();

    PropertyChanged += (_, e) =>
    {
      if (e.Property != BoundsProperty) return;
      if (DataContext is GalleryViewModel vm)
        vm.Columns = ColumnsForWidth(Bounds.Width);
    };
  }

  private void Rebind()
  {
    Unsubscribe();
    if (DataContext is GalleryViewModel vm)
    {
      _subscribed = vm;
      vm.Columns = ColumnsForWidth(Bounds.Width);
      vm.PropertyChanged += OnVmPropertyChanged;
      vm.Cameras.CollectionChanged += OnCamerasChanged;
      vm.CameraEventReceived += OnCameraEvent;
      _ = vm.LoadAsync(CancellationToken.None);
    }
  }

  private void Unsubscribe()
  {
    if (_subscribed == null) return;
    _subscribed.PropertyChanged -= OnVmPropertyChanged;
    _subscribed.Cameras.CollectionChanged -= OnCamerasChanged;
    _subscribed.CameraEventReceived -= OnCameraEvent;
    _subscribed = null;
  }

  private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

  private static int ColumnsForWidth(double width) =>
    width >= WideLayoutBreakpointDip ? 2 : 1;

  private void OnItemClicked(int index)
  {
    if (DataContext is not GalleryViewModel vm || index >= vm.Cameras.Count) return;
    FindShellViewModel()?.NavigateToCamera(vm.Cameras[index].Id);
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
    if (DataContext is GalleryViewModel vm && _emptyState != null)
      _emptyState.IsVisible = !vm.IsLoading && vm.Cameras.Count == 0;
  }

  private void OnCameraEvent(Guid cameraId)
  {
    if (DataContext is not GalleryViewModel vm || _grid == null) return;
    for (var i = 0; i < vm.Cameras.Count; i++)
    {
      if (vm.Cameras[i].Id == cameraId)
      {
        _grid.FlashCamera(i);
        return;
      }
    }
  }

  private MainShellViewModel? FindShellViewModel()
  {
    Visual? v = this;
    while (v != null)
    {
      if (v is Control c && c.DataContext is MainShellViewModel vm) return vm;
      v = v.GetVisualParent();
    }
    return null;
  }
}
