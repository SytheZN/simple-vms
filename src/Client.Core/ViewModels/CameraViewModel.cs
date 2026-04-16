using Client.Core.Api;
using Client.Core.Decoding;
using Client.Core.Streaming;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging;
using Shared.Models.Dto;

namespace Client.Core.ViewModels;

public sealed class CameraViewModel : ViewModelBase, IAsyncDisposable
{
  private readonly IApiClient _api;
  private readonly ILiveStreamService _live;
  private readonly IPlaybackService _playback;
  private readonly ITunnelService _tunnel;
  private readonly ILogger<CameraViewModel> _logger;
  private readonly ILoggerFactory _loggerFactory;

  private CameraListItem? _camera;
  private IVideoFeed? _motionFeed;
  private Player? _player;
  private string _selectedProfile = "main";
  private bool _motionOverlay;
  private long _currentPositionUs;
  private bool _isBuffering;
  private bool _isPaused;

  public CameraListItem? Camera
  {
    get => _camera;
    set => SetProperty(ref _camera, value);
  }

  public Player? Player
  {
    get => _player;
    private set => SetProperty(ref _player, value);
  }

  public IVideoFeed? MotionFeed
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

  public bool IsPlayback => _player?.Mode == Decoding.Player.PlayerMode.Playback;

  public long CurrentPositionUs
  {
    get => _currentPositionUs;
    private set => SetProperty(ref _currentPositionUs, value);
  }

  public bool IsBuffering
  {
    get => _isBuffering;
    private set => SetProperty(ref _isBuffering, value);
  }

  public bool IsPaused
  {
    get => _isPaused;
    private set => SetProperty(ref _isPaused, value);
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

  public bool IsTunnelConnected => _tunnel.State == ConnectionState.Connected;

  public CameraViewModel(IApiClient api, ILiveStreamService live, IPlaybackService playback,
    ITunnelService tunnel, ILogger<CameraViewModel> logger, ILoggerFactory loggerFactory)
  {
    _api = api;
    _live = live;
    _playback = playback;
    _tunnel = tunnel;
    _logger = logger;
    _loggerFactory = loggerFactory;
  }

  /// <summary>
  /// Wait for the tunnel to report Connected. Returns true once connected,
  /// false if the timeout elapses or the token is cancelled.
  /// </summary>
  public async Task<bool> WaitForTunnelConnectedAsync(TimeSpan timeout, CancellationToken ct)
  {
    if (_tunnel.State == ConnectionState.Connected) return true;

    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    void OnState(ConnectionState s)
    {
      if (s == ConnectionState.Connected) tcs.TrySetResult(true);
    }
    _tunnel.StateChanged += OnState;
    try
    {
      if (_tunnel.State == ConnectionState.Connected) return true;
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      cts.CancelAfter(timeout);
      using (cts.Token.Register(() => tcs.TrySetResult(false)))
        return await tcs.Task;
    }
    finally
    {
      _tunnel.StateChanged -= OnState;
    }
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

    if (_camera != null && _player == null)
    {
      _player = new Player(_loggerFactory, _live, _playback);
      _player.CurrentPositionChanged += OnPlayerPosition;
      _player.BufferingChanged += OnPlayerBuffering;
      _player.ModeChanged += OnPlayerMode;
      _player.PausedChanged += OnPlayerPaused;
      _player.Configure(_camera.Id, _selectedProfile);
      Player = _player;
    }
  }

  public Task GoLiveAsync(CancellationToken ct) =>
    _player?.GoLiveAsync(ct) ?? Task.CompletedTask;

  public Task StartPlaybackAsync(ulong from, ulong? to, CancellationToken ct) =>
    _player?.SeekAsync((long)from, ct) ?? Task.CompletedTask;

  public Task SeekAsync(ulong timestamp, CancellationToken ct) =>
    _player?.SeekAsync((long)timestamp, ct) ?? Task.CompletedTask;

  public void SetRate(double rate) => _player?.SetRate(rate);

  public void TogglePause() => _player?.TogglePause();

  public void ScrubStart() => _player?.ScrubStart();

  public void ScrubMove(long ts) => _player?.ScrubMove(ts);

  public Task ScrubEndAsync(long ts, CancellationToken ct) =>
    _player?.ScrubEndAsync(ts, ct) ?? Task.CompletedTask;

  private Task SwitchProfileAsync()
  {
    if (_player == null || _camera == null) return Task.CompletedTask;
    _logger.LogDebug("Switching profile to {Profile}", _selectedProfile);
    return _player.SetProfileAsync(_selectedProfile, CancellationToken.None);
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

  private void OnPlayerPosition(long posUs) => RunOnUiThread(() => CurrentPositionUs = posUs);
  private void OnPlayerBuffering(bool buffering) => RunOnUiThread(() => IsBuffering = buffering);
  private void OnPlayerMode(Decoding.Player.PlayerMode _) =>
    RunOnUiThread(() => OnPropertyChanged(nameof(IsPlayback)));
  private void OnPlayerPaused(bool paused) => RunOnUiThread(() => IsPaused = paused);

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
    if (_player != null)
    {
      _player.CurrentPositionChanged -= OnPlayerPosition;
      _player.BufferingChanged -= OnPlayerBuffering;
      _player.ModeChanged -= OnPlayerMode;
      _player.PausedChanged -= OnPlayerPaused;
      await _player.DetachAsync();
      _player.Dispose();
      _player = null;
    }
    if (_motionFeed != null)
    {
      await _live.UnsubscribeAsync(_motionFeed, CancellationToken.None);
      MotionFeed = null;
    }
  }
}
