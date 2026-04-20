using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Android.ViewModels;
using Client.Core.Controls;
using Client.Core.Decoding.Diagnostics;
using Client.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Application = Avalonia.Application;
using Button = Avalonia.Controls.Button;

namespace Client.Android.Views;

public sealed partial class CameraPage : UserControl
{
  private StreamQualitySelector? _qualitySelector;
  private PhosphorIcon? _playPauseIcon;
  private PhosphorIcon? _fullscreenIcon;
  private PhosphorIcon? _fullscreenIconBar;
  private Button? _fullscreenButton;
  private Button? _fullscreenButtonBar;
  private DockPanel? _headerPanel;
  private ErrorCard? _errorCardPanel;
  private Border? _playerCard;
  private Border? _videoArea;
  private StackPanel? _controlsStrip;
  private TextBlock? _timestampLabel;
  private Border? _modeBadge;
  private Ellipse? _modeDot;
  private TextBlock? _modeLabel;
  private Timeline? _timeline;
  private Panel? _bufferingOverlay;
  private Client.Core.Controls.VideoPlayer? _videoPlayer;
  private PlaybackStatsOverlay? _statsOverlay;
  private MainShellViewModel? _shellVm;
  private DispatcherTimer? _idleTimer;
  private bool _isFullscreen;
  private CancellationTokenSource? _lifetimeCts;

  public CameraPage()
  {
    InitializeComponent();

    _qualitySelector = this.FindControl<StreamQualitySelector>("QualitySelector");
    _playPauseIcon = this.FindControl<PhosphorIcon>("PlayPauseIcon");
    _fullscreenIcon = this.FindControl<PhosphorIcon>("FullscreenIcon");
    _fullscreenIconBar = this.FindControl<PhosphorIcon>("FullscreenIconBar");
    _fullscreenButton = this.FindControl<Button>("FullscreenButton");
    _fullscreenButtonBar = this.FindControl<Button>("FullscreenButtonBar");
    _headerPanel = this.FindControl<DockPanel>("HeaderPanel");
    _errorCardPanel = this.FindControl<ErrorCard>("ErrorCardPanel");
    _playerCard = this.FindControl<Border>("PlayerCard");
    _videoArea = this.FindControl<Border>("VideoArea");
    _controlsStrip = this.FindControl<StackPanel>("ControlsStrip");
    _timestampLabel = this.FindControl<TextBlock>("TimestampLabel");
    _modeBadge = this.FindControl<Border>("ModeBadge");
    _modeDot = this.FindControl<Ellipse>("ModeDot");
    _modeLabel = this.FindControl<TextBlock>("ModeLabel");
    _timeline = this.FindControl<Timeline>("TimelineControl");
    _bufferingOverlay = this.FindControl<Panel>("BufferingOverlay");
    _videoPlayer = this.FindControl<Client.Core.Controls.VideoPlayer>("VideoPlayerControl");
    _statsOverlay = this.FindControl<PlaybackStatsOverlay>("StatsOverlay");

    this.FindControl<Button>("BackButton")!.Click += OnBack;
    this.FindControl<Button>("PlayPauseButton")!.Click += OnPlayPause;
    this.FindControl<Button>("GoLiveButton")!.Click += OnGoLive;
    this.FindControl<Button>("MotionToggle")!.Click += OnMotionToggle;
    if (_fullscreenButton != null) _fullscreenButton.Click += OnFullscreen;
    if (_fullscreenButtonBar != null) _fullscreenButtonBar.Click += OnFullscreen;
    if (_qualitySelector != null) _qualitySelector.ProfileChanged += OnProfileChanged;
    if (_timeline != null) _timeline.Scrubbed += OnScrubbed;

    _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
    _idleTimer.Tick += OnIdleTimerTick;
    AddHandler(PointerReleasedEvent, OnAnyPointerReleased, handledEventsToo: true);
    if (_controlsStrip != null)
      _controlsStrip.SizeChanged += (_, _) => UpdateVideoAreaMargin();

    var tapZone = this.FindControl<Border>("DiagnosticsTapZone");
    if (tapZone != null && !IsTelevision)
    {
      var tracker = new TripleTapTracker();
      tapZone.PointerReleased += (_, _) =>
      {
        if (!tracker.Record()) return;
        var diagnostics = ((AndroidApp)Avalonia.Application.Current!).Services
          .GetService<DiagnosticsSettings>();
        if (diagnostics != null) diagnostics.ShowOverlay = !diagnostics.ShowOverlay;
      };
    }

    SetModeDisconnected();
  }

  private static bool IsTelevision =>
    (global::Android.App.Application.Context.Resources?.Configuration?.UiMode
      & global::Android.Content.Res.UiMode.TypeMask) == global::Android.Content.Res.UiMode.TypeTelevision;

  private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

  protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnDetachedFromVisualTree(e);
    if (DataContext is CameraViewModel vm) vm.PropertyChanged -= OnVmPropertyChanged;
    if (_shellVm != null) _shellVm.PropertyChanged -= OnShellVmPropertyChanged;
    _idleTimer?.Stop();
    RemoveHandler(PointerReleasedEvent, OnAnyPointerReleased);
    _lifetimeCts?.Cancel();
    _lifetimeCts?.Dispose();
    _lifetimeCts = null;
  }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnAttachedToVisualTree(e);
    _lifetimeCts?.Dispose();
    _lifetimeCts = new CancellationTokenSource();
    if (DataContext is not CameraViewModel vm) return;

    vm.PropertyChanged += OnVmPropertyChanged;

    _shellVm = FindShellViewModel();
    if (_shellVm != null)
    {
      _shellVm.PropertyChanged += OnShellVmPropertyChanged;
      ApplyFullscreen(_shellVm.IsFullscreen);
    }
    var cameraId = _shellVm?.SelectedCameraId;
    if (cameraId == null) return;

    _ = InitAsync(vm, cameraId.Value);
  }

  private async Task InitAsync(CameraViewModel vm, Guid cameraId)
  {
    var ct = _lifetimeCts?.Token ?? CancellationToken.None;
    try
    {
      await vm.LoadAsync(cameraId, ct);
      if (_videoPlayer != null) _videoPlayer.Player = vm.Player;
      if (vm.Player != null && _statsOverlay != null)
        _statsOverlay.Diagnostics = vm.Player.Diagnostics;
      if (!vm.IsTunnelConnected)
        await vm.WaitForTunnelConnectedAsync(TimeSpan.FromSeconds(10), ct);
      await vm.GoLiveAsync(ct);

      if (vm.Camera != null && _qualitySelector != null)
        _qualitySelector.Streams = vm.Camera.Streams;

      UpdateMode(vm);
      UpdatePlayPauseIcon(vm.IsPaused);
      UpdateTimestamp((ulong)vm.CurrentPositionUs);
      if (_bufferingOverlay != null) _bufferingOverlay.IsVisible = vm.IsBuffering;

      var timelineVm = ((AndroidApp)Avalonia.Application.Current!).Services
        .GetRequiredService<TimelineViewModel>();
      timelineVm.Configure(cameraId, vm.SelectedProfile);
      var now = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
      var fourHours = 4UL * 3600 * 1_000_000;
      timelineVm.SetVisibleRange(now - fourHours, now + fourHours / 4);
      if (_timeline != null)
      {
        _timeline.ViewModel = timelineVm;
      }
      timelineVm.CurrentPosition = (ulong)vm.CurrentPositionUs;
      await timelineVm.LoadAsync(ct);
    }
    catch (Exception ex)
    {
      ((AndroidApp)Avalonia.Application.Current!).Services
        .GetRequiredService<ILogger<CameraPage>>()
        .LogError(ex, "CameraPage.InitAsync failed");
    }
  }

  private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (sender is not CameraViewModel vm) return;
    if (e.PropertyName == nameof(CameraViewModel.IsPlayback))
      UpdateMode(vm);
    else if (e.PropertyName == nameof(CameraViewModel.CurrentPositionUs))
    {
      UpdateTimestamp((ulong)vm.CurrentPositionUs);
      if (_timeline?.ViewModel != null)
        _timeline.ViewModel.CurrentPosition = (ulong)vm.CurrentPositionUs;
    }
    else if (e.PropertyName == nameof(CameraViewModel.IsPaused))
      UpdatePlayPauseIcon(vm.IsPaused);
    else if (e.PropertyName == nameof(CameraViewModel.IsBuffering))
    {
      if (_bufferingOverlay != null) _bufferingOverlay.IsVisible = vm.IsBuffering;
    }
  }

  private void SetModeDisconnected() => SetBadge("SurfaceSunkenBrush", "TextMutedBrush", "Stopped");

  private void UpdateMode(CameraViewModel vm)
  {
    if (vm.IsPlayback)
      SetBadge("WarningMutedBrush", "WarningBrush", "Playback");
    else
      SetBadge("SuccessMutedBrush", "SuccessBrush", "Live");
  }

  private void UpdatePlayPauseIcon(bool paused)
  {
    if (_playPauseIcon != null)
      _playPauseIcon.Kind = paused ? PhosphorIconKind.Play : PhosphorIconKind.Pause;
  }

  private void SetBadge(string bgKey, string fgKey, string label)
  {
    if (_modeLabel != null) _modeLabel.Text = label;
    if (Application.Current?.TryGetResource(bgKey,
          Application.Current.ActualThemeVariant, out var bg) == true && _modeBadge != null)
      _modeBadge.Background = bg as IBrush;
    if (Application.Current?.TryGetResource(fgKey,
          Application.Current.ActualThemeVariant, out var fg) == true)
    {
      var brush = fg as IBrush;
      if (_modeLabel != null) _modeLabel.Foreground = brush;
      if (_modeDot != null) _modeDot.Fill = brush;
    }
  }

  private void UpdateTimestamp(ulong timestampUs)
  {
    if (_timestampLabel == null) return;
    if (timestampUs == 0) { _timestampLabel.Text = ""; return; }
    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestampUs / 1000));
    _timestampLabel.Text = dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
  }

  private void OnBack(object? sender, RoutedEventArgs e)
  {
    if (_shellVm != null) _shellVm.IsFullscreen = false;
    _shellVm?.GoBack();
  }

  private void OnFullscreen(object? sender, RoutedEventArgs e)
  {
    if (_shellVm != null) _shellVm.IsFullscreen = !_shellVm.IsFullscreen;
  }

  private void OnShellVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(MainShellViewModel.IsFullscreen) && _shellVm != null)
      ApplyFullscreen(_shellVm.IsFullscreen);
  }

  private void ApplyFullscreen(bool fs)
  {
    _isFullscreen = fs;

    if (_headerPanel != null) _headerPanel.IsVisible = !fs;
    if (_errorCardPanel != null)
      _errorCardPanel.IsVisible = !fs
        && DataContext is CameraViewModel vm
        && !string.IsNullOrEmpty(vm.ErrorMessage);

    _playerCard?.Classes.Set("fs", fs);
    _videoArea?.Classes.Set("fs", fs);
    _controlsStrip?.Classes.Set("fs", fs);

    if (_fullscreenButton != null) _fullscreenButton.IsVisible = !fs;
    if (_fullscreenButtonBar != null) _fullscreenButtonBar.IsVisible = fs;
    if (_fullscreenIcon != null) _fullscreenIcon.Kind = PhosphorIconKind.CornersOut;
    if (_fullscreenIconBar != null) _fullscreenIconBar.Kind = PhosphorIconKind.CornersIn;

    if (_controlsStrip != null) _controlsStrip.Opacity = 1;
    UpdateVideoAreaMargin();

    _idleTimer?.Stop();
    if (fs) _idleTimer?.Start();
  }

  private void UpdateVideoAreaMargin()
  {
    if (_videoArea == null || _controlsStrip == null) return;
    var bottom = _isFullscreen ? 0 : _controlsStrip.Bounds.Height;
    _videoArea.Margin = new Thickness(0, 0, 0, bottom);
  }

  private void OnAnyPointerReleased(object? sender, PointerReleasedEventArgs e)
  {
    if (!_isFullscreen) return;
    if (_controlsStrip != null) _controlsStrip.Opacity = 1;
    _idleTimer?.Stop();
    _idleTimer?.Start();
  }

  private void OnIdleTimerTick(object? sender, EventArgs e)
  {
    if (!_isFullscreen) return;
    if (_controlsStrip != null) _controlsStrip.Opacity = 0;
    _idleTimer?.Stop();
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

  private void OnPlayPause(object? sender, RoutedEventArgs e)
  {
    if (DataContext is CameraViewModel vm) vm.TogglePause();
  }

  private void OnGoLive(object? sender, RoutedEventArgs e)
  {
    if (DataContext is CameraViewModel vm)
      _ = vm.GoLiveAsync(_lifetimeCts?.Token ?? CancellationToken.None);
  }

  private void OnMotionToggle(object? sender, RoutedEventArgs e)
  {
    if (DataContext is not CameraViewModel vm) return;
    vm.MotionOverlay = !vm.MotionOverlay;
    this.FindControl<Button>("MotionToggle")!.Opacity = vm.MotionOverlay ? 1.0 : 0.5;
  }

  private void OnProfileChanged(string profile)
  {
    if (DataContext is CameraViewModel vm) vm.SelectedProfile = profile;
  }

  private void OnScrubbed(ulong timestamp)
  {
    if (DataContext is CameraViewModel vm)
      _ = vm.StartPlaybackAsync(timestamp, null, _lifetimeCts?.Token ?? CancellationToken.None);
  }
}
