using System.Collections.ObjectModel;
using Client.Core.Api;
using Client.Core.Events;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging;
using Shared.Models.Dto;
using Shared.Protocol;

namespace Client.Core.ViewModels;

public sealed class GalleryViewModel : ViewModelBase, IDisposable
{
  private readonly IApiClient _api;
  private readonly ITunnelService _tunnel;
  private readonly IEventService _events;
  private readonly ILogger<GalleryViewModel> _logger;

  private int _columns = 3;
  private CameraListItem? _selectedCamera;
  private bool _isLoading;

  public ObservableCollection<CameraListItem> Cameras { get; } = [];

  public int Columns
  {
    get => _columns;
    set => SetProperty(ref _columns, value);
  }

  public CameraListItem? SelectedCamera
  {
    get => _selectedCamera;
    set => SetProperty(ref _selectedCamera, value);
  }

  public bool IsLoading
  {
    get => _isLoading;
    private set => SetProperty(ref _isLoading, value);
  }

  public event Action<Guid>? CameraEventReceived;

  public GalleryViewModel(IApiClient api, ITunnelService tunnel, IEventService events,
    ILogger<GalleryViewModel> logger)
  {
    _api = api;
    _tunnel = tunnel;
    _events = events;
    _logger = logger;
    _tunnel.StateChanged += OnStateChanged;
    _events.OnEvent += OnEvent;
  }

  public async Task LoadAsync(CancellationToken ct)
  {
    if (_tunnel.State != ConnectionState.Connected)
    {
      _logger.LogDebug("Skipping camera load, tunnel not connected");
      return;
    }
    _logger.LogDebug("Loading cameras");
    IsLoading = true;
    var result = await _api.GetCamerasAsync(ct: ct);
    result.Switch(
      cameras => RunOnUiThread(() =>
      {
        ClearError();
        Cameras.Clear();
        foreach (var camera in cameras)
          Cameras.Add(camera);
        IsLoading = false;
        _logger.LogDebug("Loaded {Count} cameras", cameras.Count);
      }),
      error =>
      {
        _logger.LogWarning("Failed to load cameras: {Message}", error.Message);
        RunOnUiThread(() =>
        {
          IsLoading = false;
          SetError(error);
        });
      });
  }

  private void OnStateChanged(ConnectionState state)
  {
    if (state == ConnectionState.Connected)
    {
      _logger.LogDebug("Tunnel connected, reloading cameras");
      _ = LoadAsync(CancellationToken.None);
    }
  }

  private void OnEvent(EventChannelMessage msg, EventChannelFlags flags)
  {
    if ((flags & EventChannelFlags.Start) == 0) return;
    RunOnUiThread(() => CameraEventReceived?.Invoke(msg.CameraId));
  }

  public void Dispose()
  {
    _tunnel.StateChanged -= OnStateChanged;
    _events.OnEvent -= OnEvent;
  }
}
