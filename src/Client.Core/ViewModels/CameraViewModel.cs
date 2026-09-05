using Client.Core.Api;
using Client.Core.Decoding;
using Client.Core.Streaming;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging;
using Shared.Api;
using Shared.Models;
using Shared.Protocol;

namespace Client.Core.ViewModels;

public sealed class CameraViewModel : ViewModelBase, IAsyncDisposable
{
  private readonly IApiClient _api;
  private readonly ILiveStreamService _live;
  private readonly IPlaybackService _playback;
  private readonly ITunnelService _tunnel;
  private readonly ILogger<CameraViewModel> _logger;
  private readonly ILoggerFactory _loggerFactory;
  private readonly DecodePipelineFactory _decodeFactory;
  private readonly Decoding.Diagnostics.DiagnosticsSettings _diagnosticsSettings;

  private CameraDto? _camera;
  private IVideoFeed? _motionFeed;
  private bool _motionFeedIsLive;
  private MotionOverlay? _overlay;
  private Player? _player;
  private string _selectedProfile = "";
  private IReadOnlyList<StreamProfileDto> _overlaySources = [];
  private StreamProfileDto? _activeOverlaySource;
  private readonly HashSet<string> _triedLiveProfiles = [];
  private long _currentPositionUs;
  private bool _isBuffering;
  private bool _isPaused;

  public CameraDto? Camera
  {
    get => _camera;
    set => SetProperty(ref _camera, value);
  }

  public Player? Player
  {
    get => _player;
    private set => SetProperty(ref _player, value);
  }

  public MotionOverlay? Overlay
  {
    get => _overlay;
    private set => SetProperty(ref _overlay, value);
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

  public IReadOnlyList<StreamProfileDto> OverlaySources
  {
    get => _overlaySources;
    private set => SetProperty(ref _overlaySources, value);
  }

  public StreamProfileDto? ActiveOverlaySource => _activeOverlaySource;

  public bool IsTunnelConnected => _tunnel.State == ConnectionState.Connected;

  public CameraViewModel(IApiClient api, ILiveStreamService live, IPlaybackService playback,
    ITunnelService tunnel, ILogger<CameraViewModel> logger, ILoggerFactory loggerFactory,
    DecodePipelineFactory decodeFactory, Decoding.Diagnostics.DiagnosticsSettings diagnosticsSettings)
  {
    _api = api;
    _live = live;
    _playback = playback;
    _tunnel = tunnel;
    _logger = logger;
    _loggerFactory = loggerFactory;
    _decodeFactory = decodeFactory;
    _diagnosticsSettings = diagnosticsSettings;
    _live.FeedReplaced += OnLiveFeedReplaced;
  }

  private void OnLiveFeedReplaced(IVideoFeed oldFeed, IVideoFeed newFeed)
  {
    if (!ReferenceEquals(oldFeed, _motionFeed)) return;
    if (_motionFeedIsLive)
    {
      oldFeed.OnStatus -= OnOverlayLiveStatus;
      newFeed.OnStatus += OnOverlayLiveStatus;
    }
    _motionFeed = newFeed;
    _overlay?.AttachFeed(newFeed);
  }

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

  public async Task LoadAsync(Guid cameraId, Quality preferred, CancellationToken ct)
  {
    _logger.LogDebug("Loading camera {CameraId}", cameraId);
    var result = await _api.GetCameraAsync(cameraId, ct);
    result.Switch(
      camera =>
      {
        ClearError();
        Camera = camera;
        OverlaySources = camera.Streams
          .Where(s => s.Kind == StreamKind.Metadata)
          .ToList();
        OnPropertyChanged(nameof(HasOverlaySources));
        _logger.LogDebug("Camera loaded: {Name}", camera.Name);
      },
      error =>
      {
        _logger.LogWarning("Failed to load camera {CameraId}: {Message}", cameraId, error.Message);
        SetError(error);
      });

    if (_camera != null)
    {
      _selectedProfile = _camera.Streams.FirstPreferred(preferred)?.Profile ?? "";
      OnPropertyChanged(nameof(SelectedProfile));
    }

    if (_camera != null && _player == null)
    {
      var pipeline = _decodeFactory.Create(DecodeRole.Main);
      if (pipeline == null)
      {
        _logger.LogWarning("Decode pipeline unavailable; not creating Player");
        return;
      }
      _player = new Player(_loggerFactory, pipeline.Value.Backend, pipeline.Value.Renderer, _live, _playback, _diagnosticsSettings);
      _player.CurrentPositionChanged += OnPlayerPosition;
      _player.BufferingChanged += OnPlayerBuffering;
      _player.ModeChanged += OnPlayerMode;
      _player.PausedChanged += OnPlayerPaused;
      _player.Configure(_camera.Id, _selectedProfile);
      Player = _player;
    }
  }

  public async Task GoLiveAsync(CancellationToken ct)
  {
    if (_player != null)
      await _player.GoLiveAsync(ct);
    if (_motionFeed == null || Camera == null || _activeOverlaySource == null) return;

    await CloseOverlayFeedAsync(ct);
    _triedLiveProfiles.Clear();
    var source = OverlayCandidates().FirstOrDefault();
    if (source == null)
    {
      _activeOverlaySource = null;
      OnPropertyChanged(nameof(ActiveOverlaySource));
      _overlay?.Dispose();
      Overlay = null;
      return;
    }
    _triedLiveProfiles.Add(source.Profile);
    _activeOverlaySource = source;
    OnPropertyChanged(nameof(ActiveOverlaySource));
    await OpenOverlayFeedAsync(source.Profile, live: true, ct);
  }

  public async Task StartPlaybackAsync(ulong from, ulong? to, CancellationToken ct)
  {
    if (_player != null)
      await _player.SeekAsync((long)from, ct);
    _overlay?.OnSeek();
  }

  public async Task SeekAsync(ulong timestamp, CancellationToken ct)
  {
    if (_player != null)
      await _player.SeekAsync((long)timestamp, ct);
    _overlay?.OnSeek();
  }

  public void SetRate(double rate) => _player?.SetRate(rate);

  public void TogglePause() => _player?.TogglePause();

  public void ScrubStart() => _player?.ScrubStart();

  public void ScrubMove(long ts) => _player?.ScrubMove(ts);

  public async Task ScrubEndAsync(long ts, CancellationToken ct)
  {
    if (_player != null)
      await _player.ScrubEndAsync(ts, ct);
    _overlay?.OnSeek();
  }

  public async Task ToggleOverlayAsync(CancellationToken ct)
  {
    if (Camera == null) return;

    if (_activeOverlaySource != null)
    {
      await CloseOverlayFeedAsync(ct);
      _activeOverlaySource = null;
      OnPropertyChanged(nameof(ActiveOverlaySource));
      _overlay?.Dispose();
      Overlay = null;
      _triedLiveProfiles.Clear();
      return;
    }

    var live = _player?.Mode != Decoding.Player.PlayerMode.Playback;
    _triedLiveProfiles.Clear();
    var source = live
      ? OverlayCandidates().FirstOrDefault()
      : await ResolveOverlaySourceAsync(ct);
    if (source == null) return;

    if (live) _triedLiveProfiles.Add(source.Profile);
    _activeOverlaySource = source;
    OnPropertyChanged(nameof(ActiveOverlaySource));
    _logger.LogDebug("Enabling motion overlay: {Profile}", source.Profile);
    await OpenOverlayFeedAsync(source.Profile, live, ct);
  }

  private async Task AdvanceLiveOverlayAsync()
  {
    if (Camera == null || _activeOverlaySource == null) return;
    var next = OverlayCandidates().FirstOrDefault(c => !_triedLiveProfiles.Contains(c.Profile));
    _logger.LogDebug("Overlay live source {Profile} rejected, advancing to {Next}",
      _activeOverlaySource.Profile, next?.Profile ?? "(none)");

    await CloseOverlayFeedAsync(CancellationToken.None);
    if (next == null)
    {
      _activeOverlaySource = null;
      OnPropertyChanged(nameof(ActiveOverlaySource));
      _overlay?.Dispose();
      Overlay = null;
      return;
    }

    _triedLiveProfiles.Add(next.Profile);
    _activeOverlaySource = next;
    OnPropertyChanged(nameof(ActiveOverlaySource));
    await OpenOverlayFeedAsync(next.Profile, live: true, CancellationToken.None);
  }

  private void OnOverlayLiveStatus(StreamStatus status)
  {
    if (status != StreamStatus.Error) return;
    if (!_motionFeedIsLive) return;
    _ = SafeAsync(AdvanceLiveOverlayAsync);
  }

  public bool HasOverlaySources => OverlayCandidates().Any();

  private const string MotionSuffix = "-motion-grid";
  private const long OverlayWindowUs = 30_000_000;

  private IEnumerable<StreamProfileDto> OverlayCandidates()
  {
    var matching = _selectedProfile + MotionSuffix;
    return _overlaySources
      .Where(s => s.Codec == "mgrd")
      .OrderBy(s => s.Profile == matching ? 0 : 1)
      .ThenBy(s => s.Profile, StringComparer.Ordinal);
  }

  private async Task<StreamProfileDto?> ResolveOverlaySourceAsync(CancellationToken ct)
  {
    if (Camera == null) return null;
    var playheadUs = (ulong)Math.Max(0, _player?.CurrentPositionUs ?? 0);
    var windowEnd = playheadUs + (ulong)OverlayWindowUs;
    foreach (var candidate in OverlayCandidates())
    {
      var result = await _api.GetTimelineAsync(Camera.Id, playheadUs, windowEnd, candidate.Profile, ct);
      var hasData = false;
      result.Switch(
        timeline => hasData = timeline.Spans.Any(s => s.EndTime > playheadUs && s.StartTime < windowEnd),
        _ => { });
      if (hasData) return candidate;
    }
    return null;
  }

  private async Task OpenOverlayFeedAsync(string profile, bool live, CancellationToken ct)
  {
    if (Camera == null) return;

    var playheadUs = Math.Max(0, _player?.CurrentPositionUs ?? 0);
    var feed = live
      ? await _live.SubscribeAsync(Camera.Id, profile, ct)
      : await _playback.StartAsync(Camera.Id, profile, (ulong)playheadUs, (ulong)playheadUs, ct);

    _motionFeed = feed;
    _motionFeedIsLive = live;
    if (live) feed.OnStatus += OnOverlayLiveStatus;
    if (_overlay == null)
      Overlay = new MotionOverlay(_loggerFactory.CreateLogger<MotionOverlay>());
    _overlay!.AttachFeed(feed);
  }

  private async Task CloseOverlayFeedAsync(CancellationToken ct)
  {
    var feed = _motionFeed;
    if (feed == null) return;
    _motionFeed = null;
    if (_motionFeedIsLive) feed.OnStatus -= OnOverlayLiveStatus;
    _overlay?.DetachFeed();
    if (_motionFeedIsLive)
      await _live.UnsubscribeAsync(feed, ct);
    else
      await _playback.StopAsync(feed, ct);
  }

  private async Task SwitchProfileAsync()
  {
    if (_player == null || _camera == null) return;
    _logger.LogDebug("Switching profile to {Profile}", _selectedProfile);
    await _player.SetProfileAsync(_selectedProfile, CancellationToken.None);

    if (_activeOverlaySource == null) return;

    var live = _player.Mode != Decoding.Player.PlayerMode.Playback;
    _triedLiveProfiles.Clear();
    var best = live
      ? OverlayCandidates().FirstOrDefault()
      : await ResolveOverlaySourceAsync(CancellationToken.None);
    if (best == null || best.Profile == _activeOverlaySource.Profile)
    {
      if (best != null && live) _triedLiveProfiles.Add(best.Profile);
      return;
    }

    await CloseOverlayFeedAsync(CancellationToken.None);
    if (live) _triedLiveProfiles.Add(best.Profile);
    _activeOverlaySource = best;
    OnPropertyChanged(nameof(ActiveOverlaySource));
    await OpenOverlayFeedAsync(best.Profile, live, CancellationToken.None);
  }

  private void OnPlayerPosition(long posUs)
  {
    _overlay?.Tick(new OverlayPlayerView(
      posUs,
      _player?.Rate ?? 1,
      _player?.Direction ?? 1,
      _player?.Paused ?? false,
      _player?.Mode ?? Decoding.Player.PlayerMode.Live));
    RunOnUiThread(() => CurrentPositionUs = posUs);
  }
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
    _live.FeedReplaced -= OnLiveFeedReplaced;
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
    await CloseOverlayFeedAsync(CancellationToken.None);
    _overlay?.Dispose();
    Overlay = null;
  }
}
