using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace Client.Android.ViewModels;

public sealed class MainShellViewModel : ViewModelBase, IDisposable
{
  public enum ViewKind { Gallery, Camera, Settings, Enrollment }

  private readonly IServiceProvider _services;
  private readonly ITunnelService _tunnel;
  private readonly ICredentialStore _credentials;
  private readonly ILogger<MainShellViewModel> _logger;

  private ViewKind _currentView = ViewKind.Enrollment;
  private Guid? _selectedCameraId;
  private GalleryViewModel? _galleryViewModel;
  private ViewModelBase? _overlayViewModel;
  private ConnectionState _connectionState;
  private bool _isWideLayout;
  private bool _isSidebarCollapsed;
  private bool _isFullscreen;
  private readonly EventHandler _themeVariantChangedHandler;

  public MainShellViewModel(
    IServiceProvider services,
    ITunnelService tunnel,
    ICredentialStore credentials,
    ILogger<MainShellViewModel> logger)
  {
    _services = services;
    _tunnel = tunnel;
    _credentials = credentials;
    _logger = logger;
    _tunnel.StateChanged += OnTunnelStateChanged;
    ConnectionState = _tunnel.State;
    _themeVariantChangedHandler = (_, _) => OnPropertyChanged(nameof(RootBackground));
    if (Avalonia.Application.Current is { } app)
      app.ActualThemeVariantChanged += _themeVariantChangedHandler;

    NavGalleryCommand = new RelayCommand(() => NavigateTo(ViewKind.Gallery));
    NavCameraCommand = new RelayCommand(() => NavigateTo(ViewKind.Camera));
    NavSettingsCommand = new RelayCommand(() => NavigateTo(ViewKind.Settings));
    GoBackCommand = new RelayCommand(GoBack);
    ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
    ToggleFullscreenCommand = new RelayCommand(() => IsFullscreen = !IsFullscreen);

    _ = InitAsync();
  }

  public ViewKind CurrentView
  {
    get => _currentView;
    private set
    {
      if (SetProperty(ref _currentView, value))
      {
        OnPropertyChanged(nameof(IsGallery));
        OnPropertyChanged(nameof(IsCamera));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsEnrolled));
        OnPropertyChanged(nameof(IsChromeVisible));
        OnPropertyChanged(nameof(IsWideNavVisible));
        OnPropertyChanged(nameof(IsCompactNavVisible));
        OnPropertyChanged(nameof(IsSidebarToggleVisible));
        OnPropertyChanged(nameof(IsSidebarRailVisible));
      }
    }
  }

  public bool IsGallery => _currentView == ViewKind.Gallery;
  public bool IsCamera => _currentView == ViewKind.Camera;
  public bool IsSettings => _currentView == ViewKind.Settings;
  public bool IsEnrolled => _currentView != ViewKind.Enrollment;
  public bool IsChromeVisible => IsEnrolled && !IsFullscreen;
  public bool IsCameraFullscreen => IsFullscreen && _currentView == ViewKind.Camera;
  public Thickness ContentPadding => IsFullscreen ? default : new Thickness(16);

  public IBrush RootBackground
  {
    get
    {
      if (IsFullscreen) return Brushes.Black;
      var app = Avalonia.Application.Current;
      if (app != null && app.TryGetResource("SurfaceBrush", app.ActualThemeVariant, out var res) && res is IBrush brush)
        return brush;
      return Brushes.Black;
    }
  }

  public bool IsFullscreen
  {
    get => _isFullscreen;
    set
    {
      if (SetProperty(ref _isFullscreen, value))
      {
        OnPropertyChanged(nameof(IsChromeVisible));
        OnPropertyChanged(nameof(IsCameraFullscreen));
        OnPropertyChanged(nameof(IsWideNavVisible));
        OnPropertyChanged(nameof(IsCompactNavVisible));
        OnPropertyChanged(nameof(IsSidebarToggleVisible));
        OnPropertyChanged(nameof(IsSidebarRailVisible));
        OnPropertyChanged(nameof(ContentPadding));
        OnPropertyChanged(nameof(RootBackground));
      }
    }
  }

  public GalleryViewModel? Gallery
  {
    get => _galleryViewModel;
    private set => SetProperty(ref _galleryViewModel, value);
  }

  public ViewModelBase? Overlay
  {
    get => _overlayViewModel;
    private set
    {
      if (SetProperty(ref _overlayViewModel, value))
        OnPropertyChanged(nameof(IsGalleryVisible));
    }
  }

  public bool IsGalleryVisible => _overlayViewModel == null;

  public ConnectionState ConnectionState
  {
    get => _connectionState;
    private set => SetProperty(ref _connectionState, value);
  }

  public bool IsWideLayout
  {
    get => _isWideLayout;
    set
    {
      if (SetProperty(ref _isWideLayout, value))
      {
        OnPropertyChanged(nameof(IsCompactLayout));
        OnPropertyChanged(nameof(IsWideNavVisible));
        OnPropertyChanged(nameof(IsCompactNavVisible));
        OnPropertyChanged(nameof(IsSidebarToggleVisible));
        OnPropertyChanged(nameof(IsSidebarRailVisible));
      }
    }
  }

  public bool IsCompactLayout => !_isWideLayout;
  public bool IsWideNavVisible => IsWideLayout && IsChromeVisible && !IsSidebarCollapsed;
  public bool IsCompactNavVisible => IsCompactLayout && IsChromeVisible;
  public bool IsSidebarToggleVisible => IsWideLayout && IsChromeVisible;
  public bool IsSidebarRailVisible => IsSidebarToggleVisible && IsSidebarCollapsed;

  public bool IsSidebarCollapsed
  {
    get => _isSidebarCollapsed;
    set
    {
      if (SetProperty(ref _isSidebarCollapsed, value))
      {
        OnPropertyChanged(nameof(IsWideNavVisible));
        OnPropertyChanged(nameof(IsSidebarRailVisible));
      }
    }
  }

  public Guid? SelectedCameraId
  {
    get => _selectedCameraId;
    private set => SetProperty(ref _selectedCameraId, value);
  }

  public ICommand NavGalleryCommand { get; }
  public ICommand NavCameraCommand { get; }
  public ICommand NavSettingsCommand { get; }
  public ICommand GoBackCommand { get; }
  public ICommand ToggleSidebarCommand { get; }
  public ICommand ToggleFullscreenCommand { get; }

  // The gallery view is kept alive underneath whatever is pushed over it so that returning to it
  // does not rebuild the grid: a rebuilt grid loses its scroll offset and re-runs the column
  // layout. Everything else is transient and disposed as it is replaced.
  public void NavigateTo(ViewKind view)
  {
    _logger.LogDebug("NavigateTo {View}", view);
    DisposeOverlayViewModel();
    CurrentView = view;

    if (view == ViewKind.Enrollment)
      DisposeGalleryViewModel();
    else
      Gallery ??= Resolve<GalleryViewModel>();

    Overlay = view switch
    {
      ViewKind.Camera => Resolve<CameraViewModel>(),
      ViewKind.Settings => Resolve<SettingsViewModel>(),
      ViewKind.Enrollment => Resolve<EnrollmentViewModel>(),
      _ => null
    };

    if (view == ViewKind.Gallery && Gallery != null)
      _ = Gallery.LoadAsync(CancellationToken.None);
  }

  public void NavigateToCamera(Guid cameraId)
  {
    SelectedCameraId = cameraId;
    NavigateTo(ViewKind.Camera);
  }

  public void GoBack()
  {
    if (CurrentView != ViewKind.Gallery && IsEnrolled)
      NavigateTo(ViewKind.Gallery);
  }

  private async Task InitAsync()
  {
    try
    {
      var creds = await _credentials.LoadAsync();
      NavigateTo(creds != null ? ViewKind.Gallery : ViewKind.Enrollment);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "InitAsync failed");
      NavigateTo(ViewKind.Enrollment);
    }
  }

  private T Resolve<T>() where T : notnull =>
    (T)_services.GetService(typeof(T))!;

  private void DisposeOverlayViewModel()
  {
    switch (_overlayViewModel)
    {
      case IAsyncDisposable ad: _ = ad.DisposeAsync(); break;
      case IDisposable d: d.Dispose(); break;
    }
  }

  private void DisposeGalleryViewModel()
  {
    _galleryViewModel?.Dispose();
    Gallery = null;
  }

  private void OnTunnelStateChanged(ConnectionState state) =>
    RunOnUiThread(() => ConnectionState = state);

  public void Dispose()
  {
    _tunnel.StateChanged -= OnTunnelStateChanged;
    if (Avalonia.Application.Current is { } app)
      app.ActualThemeVariantChanged -= _themeVariantChangedHandler;
    DisposeOverlayViewModel();
    DisposeGalleryViewModel();
  }

  private sealed class RelayCommand(Action execute) : ICommand
  {
    event EventHandler? ICommand.CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
  }
}
