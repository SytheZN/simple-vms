using System.Collections.ObjectModel;
using Client.Core.Api;
using Client.Core.Events;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging;
using Shared.Api;
using Shared.Protocol;

namespace Client.Core.ViewModels;

public sealed class GalleryViewModel : ViewModelBase, IDisposable
{
  private readonly IApiClient _api;
  private readonly ITunnelService _tunnel;
  private readonly IEventService _events;
  private readonly ILogger<GalleryViewModel> _logger;

  private int _columns = 3;
  private CameraDto? _selectedCamera;
  private bool _isLoading;

  public ObservableCollection<CameraDto> Cameras { get; } = [];

  public int Columns
  {
    get => _columns;
    set => SetProperty(ref _columns, value);
  }

  public CameraDto? SelectedCamera
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
    RunOnUiThread(() => IsLoading = Cameras.Count == 0);
    var result = await _api.GetCamerasAsync(ct: ct);
    result.Switch(
      cameras => RunOnUiThread(() =>
      {
        ClearError();
        Reconcile(cameras);
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

  private void Reconcile(IReadOnlyList<CameraDto> incoming)
  {
    for (var i = Cameras.Count - 1; i >= 0; i--)
      if (!incoming.Any(c => c.Id == Cameras[i].Id))
        Cameras.RemoveAt(i);

    for (var i = 0; i < incoming.Count; i++)
    {
      var camera = incoming[i];
      var existing = IndexOfCamera(camera.Id);
      if (existing < 0)
      {
        Cameras.Insert(i, camera);
        continue;
      }
      if (existing != i) Cameras.Move(existing, i);
      if (!SameContent(Cameras[i], camera)) Cameras[i] = camera;
    }
  }

  private int IndexOfCamera(Guid id)
  {
    for (var i = 0; i < Cameras.Count; i++)
      if (Cameras[i].Id == id) return i;
    return -1;
  }

  private static bool SameContent(CameraDto a, CameraDto b) =>
    a.Name == b.Name &&
    a.Address == b.Address &&
    a.Status == b.Status &&
    a.ProviderId == b.ProviderId &&
    a.SegmentDuration == b.SegmentDuration &&
    a.RetentionMode == b.RetentionMode &&
    a.RetentionValue == b.RetentionValue &&
    a.Capabilities.SequenceEqual(b.Capabilities) &&
    a.Streams.SequenceEqual(b.Streams, StreamProfileComparer.Instance) &&
    SameConfig(a.Config, b.Config);

  private static bool SameConfig(Dictionary<string, string>? a, Dictionary<string, string>? b)
  {
    if (a == null || b == null) return a == b;
    if (a.Count != b.Count) return false;
    foreach (var (key, value) in a)
      if (!b.TryGetValue(key, out var other) || other != value) return false;
    return true;
  }

  private sealed class StreamProfileComparer : IEqualityComparer<StreamProfileDto>
  {
    public static readonly StreamProfileComparer Instance = new();

    public bool Equals(StreamProfileDto? a, StreamProfileDto? b) =>
      a != null && b != null &&
      a.Profile == b.Profile &&
      a.Kind == b.Kind &&
      a.Codec == b.Codec &&
      a.Resolution == b.Resolution &&
      a.Fps == b.Fps &&
      a.RecordingEnabled == b.RecordingEnabled;

    public int GetHashCode(StreamProfileDto profile) =>
      HashCode.Combine(profile.Profile, profile.Kind, profile.Codec,
        profile.Resolution, profile.Fps, profile.RecordingEnabled);
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
