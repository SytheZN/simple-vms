using System.Collections.ObjectModel;
using Client.Core.Api;
using Client.Core.Tunnel;
using Shared.Models.Dto;

namespace Client.Core.ViewModels;

public sealed class GalleryViewModel : ViewModelBase, IDisposable
{
  private readonly IApiClient _api;
  private readonly ITunnelService _tunnel;

  private int _columns = 3;
  private CameraListItem? _selectedCamera;

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

  public GalleryViewModel(IApiClient api, ITunnelService tunnel)
  {
    _api = api;
    _tunnel = tunnel;
    _tunnel.StateChanged += OnStateChanged;
  }

  public async Task LoadAsync(CancellationToken ct)
  {
    var result = await _api.GetCamerasAsync(ct: ct);
    result.Switch(
      cameras => RunOnUiThread(() =>
      {
        Cameras.Clear();
        foreach (var camera in cameras)
          Cameras.Add(camera);
      }),
      _ => { });
  }

  private void OnStateChanged(ConnectionState state)
  {
    if (state == ConnectionState.Connected)
      _ = LoadAsync(CancellationToken.None);
  }

  public void Dispose() => _tunnel.StateChanged -= OnStateChanged;
}
