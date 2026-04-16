using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Client.Core.Controls;
using Client.Core.ViewModels;
using Client.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Client.Desktop.Views;

[ExcludeFromCodeCoverage]
public partial class CameraView : UserControl
{
  private static readonly double[] RateSteps =
    [-5, -4, -3, -2, -1, -0.5, -0.25, 0.25, 0.5, 1, 1.5, 2, 3, 4, 5, 8, 16];

  private readonly StreamQualitySelector _qualitySelector;
  private readonly PhosphorIcon _playPauseIcon;
  private readonly PhosphorIcon _fullscreenIcon;
  private readonly TextBlock _timestampLabel;
  private readonly TextBlock _rateLabel;
  private readonly Border _modeBadge;
  private readonly Avalonia.Controls.Shapes.Ellipse _modeDot;
  private readonly TextBlock _modeLabel;
  private readonly Slider _rateSlider;
  private readonly Canvas _rateTickCanvas;
  private readonly Timeline _timeline;
  private readonly Panel _bufferingOverlay;
  private readonly Panel _errorOverlay;
  private readonly TextBlock _overlayText;
  private readonly Client.Core.Controls.VideoPlayer _videoPlayer;

  public CameraView()
  {
    InitializeComponent();

    _qualitySelector = this.FindControl<StreamQualitySelector>("QualitySelector")!;
    _playPauseIcon = this.FindControl<PhosphorIcon>("PlayPauseIcon")!;
    _fullscreenIcon = this.FindControl<PhosphorIcon>("FullscreenIcon")!;
    _timestampLabel = this.FindControl<TextBlock>("TimestampLabel")!;
    _rateLabel = this.FindControl<TextBlock>("RateLabel")!;
    _modeBadge = this.FindControl<Border>("ModeBadge")!;
    _modeDot = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("ModeDot")!;
    _modeLabel = this.FindControl<TextBlock>("ModeLabel")!;
    _rateSlider = this.FindControl<Slider>("RateSlider")!;
    _rateTickCanvas = this.FindControl<Canvas>("RateTickCanvas")!;
    _timeline = this.FindControl<Timeline>("TimelineControl")!;
    _bufferingOverlay = this.FindControl<Panel>("BufferingOverlay")!;
    _errorOverlay = this.FindControl<Panel>("ErrorOverlay")!;
    _overlayText = this.FindControl<TextBlock>("OverlayText")!;
    _videoPlayer = this.FindControl<Client.Core.Controls.VideoPlayer>("VideoPlayerControl")!;

    _rateSlider.Maximum = RateSteps.Length - 1;
    _rateSlider.Value = Array.IndexOf(RateSteps, 1);
    BuildRateTicks();
    SetModeDisconnected();

    this.FindControl<Button>("BackButton")!.Click += OnBack;
    this.FindControl<Button>("PlayPauseButton")!.Click += OnPlayPause;
    this.FindControl<Button>("GoLiveButton")!.Click += OnGoLive;
    this.FindControl<Button>("MotionToggle")!.Click += OnMotionToggle;
    this.FindControl<Button>("FullscreenButton")!.Click += OnFullscreen;
    this.FindControl<Button>("RetryButton")!.Click += OnRetry;
    _qualitySelector.ProfileChanged += OnProfileChanged;
    _timeline.Scrubbed += OnScrubbed;
    _rateSlider.PropertyChanged += OnRateSliderChanged;

  }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnAttachedToVisualTree(e);

    if (DataContext is not CameraViewModel vm) return;

    vm.PropertyChanged += OnVmPropertyChanged;

    var mainVm = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
    var cameraId = mainVm?.SelectedCameraId;
    if (cameraId == null) return;

    _ = InitAsync(vm, cameraId.Value);
  }

  private async Task InitAsync(CameraViewModel vm, Guid cameraId)
  {
    await vm.LoadAsync(cameraId, CancellationToken.None);
    _videoPlayer.Player = vm.Player;
    if (!vm.IsTunnelConnected)
      await vm.WaitForTunnelConnectedAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    await vm.GoLiveAsync(CancellationToken.None);

    if (vm.Camera != null)
      _qualitySelector.Streams = vm.Camera.Streams;

    UpdateMode(vm);
    UpdatePlayPauseIcon(vm.IsPaused);
    UpdateTimestamp((ulong)vm.CurrentPositionUs);
    _bufferingOverlay.IsVisible = vm.IsBuffering;

    var timelineVm = ((App)Avalonia.Application.Current!).Services
      .GetRequiredService<TimelineViewModel>();
    timelineVm.Configure(cameraId, vm.SelectedProfile);
    var now = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
    var fourHours = 4UL * 3600 * 1_000_000;
    timelineVm.SetVisibleRange(now - fourHours, now + fourHours / 4);
    _timeline.ViewModel = timelineVm;
    timelineVm.CurrentPosition = (ulong)vm.CurrentPositionUs;
    await timelineVm.LoadAsync(CancellationToken.None);
  }

  private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (sender is not CameraViewModel vm) return;
    if (e.PropertyName == nameof(CameraViewModel.IsPlayback))
      UpdateMode(vm);
    else if (e.PropertyName == nameof(CameraViewModel.CurrentPositionUs))
    {
      UpdateTimestamp((ulong)vm.CurrentPositionUs);
      if (_timeline.ViewModel != null)
        _timeline.ViewModel.CurrentPosition = (ulong)vm.CurrentPositionUs;
    }
    else if (e.PropertyName == nameof(CameraViewModel.IsPaused))
      UpdatePlayPauseIcon(vm.IsPaused);
    else if (e.PropertyName == nameof(CameraViewModel.IsBuffering))
      _bufferingOverlay.IsVisible = vm.IsBuffering;
  }

  private void SetModeDisconnected()
  {
    SetBadge("SurfaceSunkenBrush", "TextMutedBrush", "Stopped");
  }

  private void UpdateMode(CameraViewModel vm)
  {
    if (vm.IsPlayback)
      SetBadge("WarningMutedBrush", "WarningBrush", "Playback");
    else
      SetBadge("SuccessMutedBrush", "SuccessBrush", "Live");

    _rateSlider.Value = Array.IndexOf(RateSteps, 1);
  }

  private void UpdatePlayPauseIcon(bool paused)
  {
    _playPauseIcon.Kind = paused
      ? PhosphorIconKind.Play
      : PhosphorIconKind.Pause;
  }

  private void SetBadge(string bgKey, string fgKey, string label)
  {
    _modeLabel.Text = label;
    if (Application.Current?.TryGetResource(bgKey,
          Application.Current.ActualThemeVariant, out var bg) == true)
      _modeBadge.Background = bg as Avalonia.Media.IBrush;
    if (Application.Current?.TryGetResource(fgKey,
          Application.Current.ActualThemeVariant, out var fg) == true)
    {
      var brush = fg as Avalonia.Media.IBrush;
      _modeLabel.Foreground = brush;
      _modeDot.Fill = brush;
    }
  }

  private void OnRateSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
  {
    if (e.Property != RangeBase.ValueProperty) return;
    var idx = (int)_rateSlider.Value;
    if (idx < 0 || idx >= RateSteps.Length) return;
    var rate = RateSteps[idx];
    _rateLabel.Text = $"{rate}x";
    if (DataContext is CameraViewModel vm)
      vm.SetRate(rate);
  }

  private void UpdateTimestamp(ulong timestampUs)
  {
    if (timestampUs == 0)
    {
      _timestampLabel.Text = "";
      return;
    }
    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestampUs / 1000));
    _timestampLabel.Text = dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
  }

  private void OnBack(object? sender, RoutedEventArgs e)
  {
    var main = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
    main?.NavigateTo(MainWindowViewModel.ViewKind.Gallery);
  }

  private void OnPlayPause(object? sender, RoutedEventArgs e)
  {
    if (DataContext is CameraViewModel vm)
      vm.TogglePause();
  }

  private void OnGoLive(object? sender, RoutedEventArgs e)
  {
    if (DataContext is CameraViewModel vm)
      _ = vm.GoLiveAsync(CancellationToken.None);
  }

  private void BuildRateTicks()
  {
    IBrush? tickBrush = null;
    IBrush? accentBrush = null;
    if (Application.Current?.TryGetResource("BorderBrush",
          Application.Current.ActualThemeVariant, out var tb) == true)
      tickBrush = tb as IBrush;
    if (Application.Current?.TryGetResource("PrimaryBrush",
          Application.Current.ActualThemeVariant, out var ab) == true)
      accentBrush = ab as IBrush;
    tickBrush ??= Brushes.Gray;
    accentBrush ??= Brushes.CornflowerBlue;

    var max = RateSteps.Length - 1;
    for (var i = 0; i <= max; i++)
    {
      var rate = RateSteps[i];
      if (rate != Math.Floor(rate)) continue;

      var pct = (double)i / max;
      var isOne = Math.Abs(rate - 1) < 0.01;
      var line = new Line
      {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, isOne ? 8 : 6),
        Stroke = isOne ? accentBrush : tickBrush,
        StrokeThickness = 1
      };
      Canvas.SetLeft(line, pct * 122);
      _rateTickCanvas.Children.Add(line);
    }
  }

  private void OnRetry(object? sender, RoutedEventArgs e)
  {
    if (DataContext is CameraViewModel vm)
    {
      _errorOverlay.IsVisible = false;
      _ = vm.GoLiveAsync(CancellationToken.None);
    }
  }

  private void OnMotionToggle(object? sender, RoutedEventArgs e)
  {
    if (DataContext is not CameraViewModel vm) return;
    vm.MotionOverlay = !vm.MotionOverlay;
    this.FindControl<Button>("MotionToggle")!.Opacity = vm.MotionOverlay ? 1.0 : 0.5;
  }

  private void OnFullscreen(object? sender, RoutedEventArgs e)
  {
    var main = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
    if (main == null) return;
    main.ToggleFullscreen();
    _fullscreenIcon.Kind = main.IsFullscreen
      ? PhosphorIconKind.CornersIn
      : PhosphorIconKind.CornersOut;
  }

  private void OnProfileChanged(string profile)
  {
    if (DataContext is CameraViewModel vm)
      vm.SelectedProfile = profile;
  }

  private void OnScrubbed(ulong timestamp)
  {
    if (DataContext is CameraViewModel vm)
      _ = vm.StartPlaybackAsync(timestamp, null, CancellationToken.None);
  }
}
