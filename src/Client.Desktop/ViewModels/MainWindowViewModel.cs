using System.Windows.Input;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace Client.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
  private readonly IServiceProvider _services;
  private readonly ITunnelService _tunnel;
  private readonly ICredentialStore _credentials;
  private readonly ILogger<MainWindowViewModel> _logger;

  private ViewKind _currentView = ViewKind.Enrollment;
  private Guid? _selectedCameraId;
  private ViewModelBase? _currentViewModel;
  private ConnectionState _connectionState;
  private bool _isFullscreen;

  public enum ViewKind { Gallery, Camera, Settings, Enrollment }

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
      }
    }
  }

  public bool IsGallery => _currentView == ViewKind.Gallery;
  public bool IsCamera => _currentView == ViewKind.Camera;
  public bool IsSettings => _currentView == ViewKind.Settings;
  public bool IsEnrolled => _currentView != ViewKind.Enrollment;

  public ViewModelBase? CurrentViewModel
  {
    get => _currentViewModel;
    private set => SetProperty(ref _currentViewModel, value);
  }

  public ConnectionState ConnectionState
  {
    get => _connectionState;
    private set => SetProperty(ref _connectionState, value);
  }

  public bool IsFullscreen
  {
    get => _isFullscreen;
    set => SetProperty(ref _isFullscreen, value);
  }

  public Guid? SelectedCameraId
  {
    get => _selectedCameraId;
    private set => SetProperty(ref _selectedCameraId, value);
  }

  public ICommand ToggleFullscreenCommand { get; }
  public ICommand GoBackCommand { get; }
  public ICommand NextCameraCommand { get; }
  public ICommand PrevCameraCommand { get; }
  public ICommand NavGalleryCommand { get; }
  public ICommand NavCameraCommand { get; }
  public ICommand NavSettingsCommand { get; }

  public MainWindowViewModel(IServiceProvider services, ITunnelService tunnel,
    ICredentialStore credentials, ILogger<MainWindowViewModel> logger)
  {
    _services = services;
    _tunnel = tunnel;
    _credentials = credentials;
    _logger = logger;
    _tunnel.StateChanged += OnTunnelStateChanged;
    ConnectionState = _tunnel.State;

    ToggleFullscreenCommand = new RelayCommand(ToggleFullscreen);
    GoBackCommand = new RelayCommand(GoBack);
    NextCameraCommand = new RelayCommand(NextCamera);
    PrevCameraCommand = new RelayCommand(PrevCamera);
    NavGalleryCommand = new RelayCommand(() => NavigateTo(ViewKind.Gallery));
    NavCameraCommand = new RelayCommand(() => NavigateTo(ViewKind.Camera));
    NavSettingsCommand = new RelayCommand(() => NavigateTo(ViewKind.Settings));

    _ = InitAsync();
  }

  private async Task InitAsync()
  {
    _logger.LogDebug("InitAsync: checking for stored credentials");
    try
    {
      var creds = await _credentials.LoadAsync();
      var hasCredentials = creds != null;
      _logger.LogDebug("InitAsync: credentials={HasCredentials}", hasCredentials);
      NavigateTo(hasCredentials ? ViewKind.Gallery : ViewKind.Enrollment);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "InitAsync: failed to load credentials");
      NavigateTo(ViewKind.Enrollment);
    }
  }

  private sealed class RelayCommand(Action execute) : ICommand
  {
    event EventHandler? ICommand.CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
  }

  public void NavigateTo(ViewKind view)
  {
    _logger.LogDebug("NavigateTo {View}", view);
    DisposeCurrentViewModel();
    CurrentView = view;
    CurrentViewModel = view switch
    {
      ViewKind.Gallery => Resolve<GalleryViewModel>(),
      ViewKind.Camera => Resolve<CameraViewModel>(),
      ViewKind.Settings => Resolve<SettingsViewModel>(),
      ViewKind.Enrollment => Resolve<EnrollmentViewModel>(),
      _ => null
    };
  }

  public void NavigateToCamera(Guid cameraId)
  {
    SelectedCameraId = cameraId;
    NavigateTo(ViewKind.Camera);
  }

  public void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

  public void NextCamera()
  {
    if (CurrentViewModel is GalleryViewModel gallery && gallery.Cameras.Count > 0)
    {
      var idx = SelectedCameraId == null ? 0
        : gallery.Cameras.ToList().FindIndex(c => c.Id == SelectedCameraId);
      idx = (idx + 1) % gallery.Cameras.Count;
      NavigateToCamera(gallery.Cameras[idx].Id);
    }
  }

  public void PrevCamera()
  {
    if (CurrentViewModel is GalleryViewModel gallery && gallery.Cameras.Count > 0)
    {
      var idx = SelectedCameraId == null ? 0
        : gallery.Cameras.ToList().FindIndex(c => c.Id == SelectedCameraId);
      idx = (idx - 1 + gallery.Cameras.Count) % gallery.Cameras.Count;
      NavigateToCamera(gallery.Cameras[idx].Id);
    }
  }

  public void GoBack()
  {
    if (IsFullscreen)
      IsFullscreen = false;
    else if (CurrentView != ViewKind.Gallery)
      NavigateTo(ViewKind.Gallery);
  }

  private T Resolve<T>() where T : notnull =>
    (T)_services.GetService(typeof(T))!;

  private void DisposeCurrentViewModel()
  {
    if (_currentViewModel is IDisposable d) d.Dispose();
    if (_currentViewModel is IAsyncDisposable ad) _ = ad.DisposeAsync();
  }

  private void OnTunnelStateChanged(ConnectionState state) =>
    RunOnUiThread(() => ConnectionState = state);

  public void Dispose()
  {
    _tunnel.StateChanged -= OnTunnelStateChanged;
    DisposeCurrentViewModel();
  }
}
