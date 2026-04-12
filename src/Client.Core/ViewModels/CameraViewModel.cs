using Client.Core.Api;
using Client.Core.Streaming;
using Microsoft.Extensions.Logging;
using Shared.Models.Dto;

namespace Client.Core.ViewModels;

public sealed class CameraViewModel : ViewModelBase, IAsyncDisposable
{
  private readonly IApiClient _api;
  private readonly ILiveStreamService _live;
  private readonly IPlaybackService _playback;
  private readonly ILogger<CameraViewModel> _logger;

  private CameraListItem? _camera;
  private VideoFeed? _videoFeed;
  private VideoFeed? _motionFeed;
  private string _selectedProfile = "main";
  private CancellationTokenSource? _switchCts;
  private bool _isPlayback;
  private bool _motionOverlay;

  public CameraListItem? Camera
  {
    get => _camera;
    set => SetProperty(ref _camera, value);
  }

  public VideoFeed? VideoFeed
  {
    get => _videoFeed;
    private set => SetProperty(ref _videoFeed, value);
  }

  public VideoFeed? MotionFeed
  {
    get => _motionFeed;
    private set => SetProperty(ref _motionFeed, value);
  }

  public string SelectedProfile
  {
    get => _selectedProfile;
    set
    {
      if (SetProperty(ref _selectedProfile, value))
        _ = SafeAsync(SwitchProfileAsync);
    }
  }

  public bool IsPlayback
  {
    get => _isPlayback;
    private set => SetProperty(ref _isPlayback, value);
  }

  public bool MotionOverlay
  {
    get => _motionOverlay;
    set
    {
      if (SetProperty(ref _motionOverlay, value))
        _ = SafeAsync(ToggleMotionAsync);
    }
  }

  public CameraViewModel(IApiClient api, ILiveStreamService live, IPlaybackService playback,
    ILogger<CameraViewModel> logger)
  {
    _api = api;
    _live = live;
    _playback = playback;
    _logger = logger;
    _live.FeedReplaced += OnFeedReplaced;
  }

  public async Task LoadAsync(Guid cameraId, CancellationToken ct)
  {
    _logger.LogDebug("Loading camera {CameraId}", cameraId);
    var result = await _api.GetCameraAsync(cameraId, ct);
    result.Switch(
      camera =>
      {
        ClearError();
        Camera = camera;
        _logger.LogDebug("Camera loaded: {Name}", camera.Name);
      },
      error =>
      {
        _logger.LogWarning("Failed to load camera {CameraId}: {Message}", cameraId, error.Message);
        SetError(error);
      });
  }

  public async Task GoLiveAsync(CancellationToken ct)
  {
    _logger.LogDebug("Going live on {Profile}", SelectedProfile);
    await StopCurrentFeedAsync();
    IsPlayback = false;

    if (Camera == null) return;
    VideoFeed = await _live.SubscribeAsync(Camera.Id, SelectedProfile, ct);
  }

  public async Task StartPlaybackAsync(ulong from, ulong? to, CancellationToken ct)
  {
    _logger.LogDebug("Starting playback from={From} to={To}", from, to);
    await StopCurrentFeedAsync();
    IsPlayback = true;

    if (Camera == null) return;
    VideoFeed = await _playback.StartAsync(Camera.Id, SelectedProfile, from, to, ct);
  }

  public async Task SeekAsync(ulong timestamp, CancellationToken ct)
  {
    if (VideoFeed == null || !IsPlayback) return;
    _logger.LogDebug("Seeking to {Timestamp}", timestamp);
    VideoFeed = await _playback.SeekAsync(VideoFeed, timestamp, ct);
  }

  private async Task SwitchProfileAsync()
  {
    if (Camera == null) return;
    if (IsPlayback) return;

    _logger.LogDebug("Switching profile to {Profile}", SelectedProfile);
    _switchCts?.Cancel();
    _switchCts?.Dispose();
    _switchCts = new CancellationTokenSource();
    var ct = _switchCts.Token;

    await StopCurrentFeedAsync();
    ct.ThrowIfCancellationRequested();
    VideoFeed = await _live.SubscribeAsync(Camera.Id, SelectedProfile, ct);
  }

  private async Task ToggleMotionAsync()
  {
    if (Camera == null) return;

    if (_motionOverlay && _motionFeed == null)
    {
      _logger.LogDebug("Enabling motion overlay");
      MotionFeed = await _live.SubscribeAsync(Camera.Id, "motion", CancellationToken.None);
    }
    else if (!_motionOverlay && _motionFeed != null)
    {
      _logger.LogDebug("Disabling motion overlay");
      await _live.UnsubscribeAsync(_motionFeed, CancellationToken.None);
      MotionFeed = null;
    }
  }

  private void OnFeedReplaced(VideoFeed oldFeed, VideoFeed newFeed)
  {
    if (_videoFeed == oldFeed)
      RunOnUiThread(() => VideoFeed = newFeed);
    if (_motionFeed == oldFeed)
      RunOnUiThread(() => MotionFeed = newFeed);
  }

  private async Task StopCurrentFeedAsync()
  {
    if (_videoFeed != null)
    {
      if (IsPlayback)
        await _playback.StopAsync(_videoFeed, CancellationToken.None);
      else
        await _live.UnsubscribeAsync(_videoFeed, CancellationToken.None);
      VideoFeed = null;
    }
  }

  private async Task SafeAsync(Func<Task> action)
  {
    try { await action(); }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Async operation failed");
    }
  }

  public async ValueTask DisposeAsync()
  {
    _live.FeedReplaced -= OnFeedReplaced;
    _switchCts?.Cancel();
    _switchCts?.Dispose();
    await StopCurrentFeedAsync();
    if (_motionFeed != null)
    {
      await _live.UnsubscribeAsync(_motionFeed, CancellationToken.None);
      MotionFeed = null;
    }
  }
}
