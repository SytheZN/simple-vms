using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Core.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class Timeline : UserControl
{
  public static readonly StyledProperty<TimelineViewModel?> ViewModelProperty =
    AvaloniaProperty.Register<Timeline, TimelineViewModel?>(nameof(ViewModel));

  public static readonly StyledProperty<double> TrackHeightProperty =
    AvaloniaProperty.Register<Timeline, double>(nameof(TrackHeight), 16);

  public static readonly StyledProperty<double> PlayheadFractionProperty =
    AvaloniaProperty.Register<Timeline, double>(nameof(PlayheadFraction), 0.8);

  private static readonly int[] HourIntervals = [1, 2, 5, 10, 15, 30, 60, 120, 240, 360, 720];
  private static readonly int[] DayIntervals = [1, 2, 3, 7];
  private const int MaxTicks = 8;
  private const double PlayheadWidth = 2;

  private readonly TimelineViewport _viewport = new();

  private readonly Panel _trackPanel;
  private readonly Canvas _spanCanvas;
  private readonly Canvas _markerCanvas;
  private readonly Canvas _playheadCanvas;
  private readonly Canvas _tickCanvas;

  private readonly List<Rectangle> _spanRects = [];
  private readonly List<Rectangle> _markerRects = [];
  private readonly List<Line> _tickLines = [];
  private readonly List<TextBlock> _tickLabels = [];
  private readonly Rectangle _playhead;

  private ISolidColorBrush? _spanBrush;
  private ISolidColorBrush? _markerBrush;
  private ISolidColorBrush? _tickBrush;
  private ISolidColorBrush? _tickTextBrush;
  private ISolidColorBrush? _playheadBrush;

  private bool _renderQueued;
  private bool _isPanning;
  private bool _mouseDragging;
  private double _mouseLastX;
  private double? _pinchStartVisibleUs;
  private ulong _loadedFrom;
  private ulong _loadedTo;

  public TimelineViewModel? ViewModel
  {
    get => GetValue(ViewModelProperty);
    set => SetValue(ViewModelProperty, value);
  }

  public double TrackHeight
  {
    get => GetValue(TrackHeightProperty);
    set => SetValue(TrackHeightProperty, value);
  }

  public double PlayheadFraction
  {
    get => GetValue(PlayheadFractionProperty);
    set => SetValue(PlayheadFractionProperty, value);
  }

  public event Action<ulong>? Scrubbed;

  static Timeline()
  {
    PinchEvent.AddClassHandler<Timeline>((t, e) => t.OnPinch(e));
    PinchEndedEvent.AddClassHandler<Timeline>((t, _) => t.OnPinchEnded());
    ScrollGestureEvent.AddClassHandler<Timeline>((t, e) => t.OnScrollGesture(e));
    ScrollGestureEndedEvent.AddClassHandler<Timeline>((t, _) => t.EndPan());
  }

  public Timeline()
  {
    InitializeComponent();
    _trackPanel = this.FindControl<Panel>("TrackPanel")!;
    _spanCanvas = this.FindControl<Canvas>("SpanCanvas")!;
    _markerCanvas = this.FindControl<Canvas>("MarkerCanvas")!;
    _playheadCanvas = this.FindControl<Canvas>("PlayheadCanvas")!;
    _tickCanvas = this.FindControl<Canvas>("TickCanvas")!;

    _trackPanel.Height = TrackHeight;

    _playhead = new Rectangle { Width = PlayheadWidth, Height = TrackHeight };
    _playheadCanvas.Children.Add(_playhead);

    GestureRecognizers.Add(new PinchGestureRecognizer());
    GestureRecognizers.Add(new ScrollGestureRecognizer
    {
      CanHorizontallyScroll = true,
      CanVerticallyScroll = false,
      IsScrollInertiaEnabled = false
    });

    ActualThemeVariantChanged += (_, _) => ResetBrushes();
  }

  public void SetPosition(ulong timestampUs)
  {
    if (_isPanning || _pinchStartVisibleUs != null) return;

    _viewport.PlayheadTimestamp = timestampUs;
    EnsureLoaded();
    InvalidateTimeline();
  }

  protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnDetachedFromVisualTree(e);
    var vm = ViewModel;
    if (vm != null)
      vm.PropertyChanged -= OnViewModelPropertyChanged;
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);

    if (change.Property == TrackHeightProperty)
    {
      _trackPanel.Height = TrackHeight;
      _playhead.Height = TrackHeight;
      InvalidateTimeline();
    }
    else if (change.Property == PlayheadFractionProperty)
    {
      InvalidateTimeline();
    }
    else if (change.Property == ViewModelProperty)
    {
      var oldVm = change.GetOldValue<TimelineViewModel?>();
      if (oldVm != null)
        oldVm.PropertyChanged -= OnViewModelPropertyChanged;

      var newVm = change.GetNewValue<TimelineViewModel?>();
      if (newVm != null)
        newVm.PropertyChanged += OnViewModelPropertyChanged;

      InvalidateTimeline();
    }
  }

  private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(TimelineViewModel.Spans) or nameof(TimelineViewModel.Events))
      InvalidateTimeline();
  }

  protected override void OnSizeChanged(SizeChangedEventArgs e)
  {
    base.OnSizeChanged(e);
    InvalidateTimeline();
  }

  // Coalesces bursts - a gesture can touch position, range and spans within one frame, and
  // each of those used to drive a full rebuild of the canvases.
  private void InvalidateTimeline()
  {
    if (_renderQueued) return;
    _renderQueued = true;
    Dispatcher.UIThread.Post(() =>
    {
      _renderQueued = false;
      Render();
    }, DispatcherPriority.Render);
  }

  private void Render()
  {
    var vm = ViewModel;
    var width = Bounds.Width;
    if (vm == null || width <= 0) return;

    _viewport.Width = width;
    _viewport.PlayheadFraction = PlayheadFraction;

    RenderSpans(vm, width);
    RenderMarkers(vm, width);
    RenderTicks(width);
    PositionPlayhead();
  }

  private void RenderSpans(TimelineViewModel vm, double width)
  {
    var pixelCount = (int)Math.Ceiling(width);
    if (pixelCount <= 0) return;

    var filled = new bool[pixelCount];
    foreach (var span in vm.Spans)
    {
      var left = Math.Max(0, (int)_viewport.XOf(span.StartTime));
      var right = Math.Min(pixelCount - 1, (int)_viewport.XOf(span.EndTime));
      for (var i = left; i <= right; i++)
        filled[i] = true;
    }

    var used = 0;
    var runStart = -1;
    for (var i = 0; i <= pixelCount; i++)
    {
      var active = i < pixelCount && filled[i];
      if (active && runStart < 0)
        runStart = i;
      else if (!active && runStart >= 0)
      {
        var rect = Take(_spanRects, _spanCanvas, used++, SpanBrush);
        rect.Width = Math.Max(1, i - runStart);
        rect.Height = TrackHeight;
        Canvas.SetLeft(rect, runStart);
        runStart = -1;
      }
    }

    Hide(_spanRects, used);
  }

  private void RenderMarkers(TimelineViewModel vm, double width)
  {
    var used = 0;
    foreach (var evt in vm.Events)
    {
      var x = _viewport.XOf(evt.StartTime);
      if (x < 0 || x > width) continue;

      var marker = Take(_markerRects, _markerCanvas, used++, MarkerBrush);
      marker.Width = 2;
      marker.Height = TrackHeight;
      Canvas.SetLeft(marker, x - 1);
    }

    Hide(_markerRects, used);
  }

  private void RenderTicks(double width)
  {
    var from = _viewport.VisibleFrom;
    var to = _viewport.VisibleTo;
    var range = _viewport.VisibleDurationUs;
    if (to <= from) return;

    var rangeMinutes = range / (60 * 1_000_000);
    int? intervalMinutes = null;
    foreach (var m in HourIntervals)
    {
      if (rangeMinutes / m <= MaxTicks) { intervalMinutes = m; break; }
    }

    var used = intervalMinutes != null
      ? RenderHourTicks(from, to, width, intervalMinutes.Value)
      : RenderDayTicks(from, to, width);

    Hide(_tickLines, used);
    Hide(_tickLabels, used);
  }

  private int RenderHourTicks(ulong from, ulong to, double width, int intervalMinutes)
  {
    var intervalUs = (long)intervalMinutes * 60 * 1_000_000L;
    var startDto = DateTimeOffset.FromUnixTimeMilliseconds((long)(from / 1000));
    var endDto = DateTimeOffset.FromUnixTimeMilliseconds((long)(to / 1000));
    var crossesDate = startDto.LocalDateTime.Date != endDto.LocalDateTime.Date;

    var midnightLocal = startDto.LocalDateTime.Date;
    var midnightOffset = TimeZoneInfo.Local.GetUtcOffset(midnightLocal);
    var localMidnight = new DateTimeOffset(midnightLocal, midnightOffset);
    var midnightUs = (ulong)(localMidnight.ToUnixTimeMilliseconds() * 1000);
    var sinceLocal = (long)from - (long)midnightUs;
    var firstTick = (long)midnightUs + ((sinceLocal / intervalUs) + 1) * intervalUs;

    var used = 0;
    for (var tickUs = firstTick; tickUs <= (long)to; tickUs += intervalUs)
    {
      var x = _viewport.XOf(tickUs);
      if (x < 0 || x > width) continue;

      var dt = DateTimeOffset.FromUnixTimeMilliseconds(tickUs / 1000).LocalDateTime;
      var label = crossesDate && dt is { Hour: 0, Minute: 0 }
        ? dt.ToString("MM/dd")
        : dt.ToString("HH:mm");

      PlaceTick(used++, x, label);
    }

    return used;
  }

  private int RenderDayTicks(ulong from, ulong to, double width)
  {
    var rangeDays = _viewport.VisibleDurationUs / (1440.0 * 60 * 1_000_000);
    var stepDays = 1;
    foreach (var d in DayIntervals)
    {
      if (rangeDays / d <= MaxTicks) { stepDays = d; break; }
    }

    var anchorDto = DateTimeOffset.FromUnixTimeMilliseconds((long)(from / 1000));
    var anchorDate = anchorDto.LocalDateTime.Date;
    var anchorMidnight = new DateTimeOffset(anchorDate, TimeZoneInfo.Local.GetUtcOffset(anchorDate));
    var stepMs = (long)stepDays * 86_400_000L;
    var anchorMs = anchorMidnight.ToUnixTimeMilliseconds();
    var startMs = (long)(from / 1000);
    var endMs = (long)(to / 1000);

    var firstStep = (int)Math.Ceiling((double)(startMs - anchorMs) / stepMs);
    var lastStep = (int)Math.Floor((double)(endMs - anchorMs) / stepMs);

    var used = 0;
    for (var i = firstStep; i <= lastStep; i++)
    {
      var tickMs = anchorMs + (long)i * stepMs;
      var x = _viewport.XOf((double)tickMs * 1000);
      if (x < 0 || x > width) continue;

      var dt = DateTimeOffset.FromUnixTimeMilliseconds(tickMs).LocalDateTime;
      PlaceTick(used++, x, dt.ToString("MM/dd"));
    }

    return used;
  }

  private void PlaceTick(int index, double x, string label)
  {
    var line = Take(_tickLines, _tickCanvas, index);
    line.StartPoint = new Point(x, 0);
    line.EndPoint = new Point(x, 4);
    line.Stroke = TickBrush;
    line.StrokeThickness = 1;

    var text = Take(_tickLabels, _tickCanvas, index);
    text.Text = label;
    text.FontSize = 10;
    text.Foreground = TickTextBrush;
    Canvas.SetLeft(text, x - 16);
    Canvas.SetTop(text, 5);
  }

  private void PositionPlayhead()
  {
    _playhead.Fill = PlayheadBrush;
    Canvas.SetLeft(_playhead, _viewport.PlayheadX - PlayheadWidth / 2);
  }

  // Children are reused across renders rather than cleared and rebuilt: the visual tree
  // stays stable so Avalonia only re-arranges, instead of re-measuring a fresh subtree.
  private static Rectangle Take(List<Rectangle> pool, Canvas canvas, int index, IBrush fill)
  {
    var rect = Take(pool, canvas, index);
    rect.Fill = fill;
    return rect;
  }

  private static T Take<T>(List<T> pool, Canvas canvas, int index) where T : Control, new()
  {
    if (index < pool.Count)
    {
      var existing = pool[index];
      existing.IsVisible = true;
      return existing;
    }

    var created = new T();
    pool.Add(created);
    canvas.Children.Add(created);
    return created;
  }

  private static void Hide<T>(List<T> pool, int used) where T : Control
  {
    for (var i = used; i < pool.Count; i++)
      pool[i].IsVisible = false;
  }

  private void OnScrollGesture(ScrollGestureEventArgs e)
  {
    if (_pinchStartVisibleUs != null) return;

    _isPanning = true;
    _viewport.PanByPixels(-e.Delta.X);
    InvalidateTimeline();
    e.Handled = true;
  }

  // Touch panning goes through ScrollGestureRecognizer, which never sees a mouse. Capturing
  // is safe here for the same reason: a pinch only ever involves touch pointers, so mouse
  // capture cannot starve the gesture recognizers.
  protected override void OnPointerPressed(PointerPressedEventArgs e)
  {
    base.OnPointerPressed(e);
    if (e.Pointer.Type != PointerType.Mouse) return;

    _mouseDragging = true;
    _isPanning = true;
    _mouseLastX = e.GetPosition(this).X;
    e.Pointer.Capture(this);
  }

  protected override void OnPointerMoved(PointerEventArgs e)
  {
    base.OnPointerMoved(e);
    if (!_mouseDragging) return;

    var x = e.GetPosition(this).X;
    _viewport.PanByPixels(x - _mouseLastX);
    _mouseLastX = x;
    InvalidateTimeline();
  }

  protected override void OnPointerReleased(PointerReleasedEventArgs e)
  {
    base.OnPointerReleased(e);

    if (_mouseDragging)
    {
      _mouseDragging = false;
      if (ReferenceEquals(e.Pointer.Captured, this))
        e.Pointer.Capture(null);
    }

    EndPan();
  }

  private void EndPan()
  {
    if (!_isPanning) return;

    _isPanning = false;
    CommitPan();
  }

  protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
  {
    base.OnPointerWheelChanged(e);

    _viewport.ZoomTo(_viewport.VisibleDurationUs * (e.Delta.Y > 0 ? 0.8 : 1.25));
    InvalidateTimeline();
    EnsureLoaded();
    e.Handled = true;
  }

  private void OnPinch(PinchEventArgs e)
  {
    if (e.Scale <= 0) return;

    _isPanning = false;
    _pinchStartVisibleUs ??= _viewport.VisibleDurationUs;
    _viewport.ZoomTo(_pinchStartVisibleUs.Value / e.Scale);
    InvalidateTimeline();
  }

  // A scroll gesture claiming the pointer makes the pinch recognizer report capture loss,
  // which surfaces as PinchEnded for a pinch that never started.
  private void OnPinchEnded()
  {
    if (_pinchStartVisibleUs == null) return;

    _pinchStartVisibleUs = null;
    EnsureLoaded();
  }

  // Only panning moves the playhead. Zoom keeps the timestamp fixed, so raising Scrubbed
  // there would seek to the position it already has - restarting the stream from the
  // nearest keyframe and visibly jumping playback.
  private void CommitPan()
  {
    EnsureLoaded();
    Scrubbed?.Invoke(_viewport.PlayheadTimestamp);
  }

  private void EnsureLoaded()
  {
    var vm = ViewModel;
    if (vm == null) return;

    var from = _viewport.VisibleFrom;
    var to = _viewport.VisibleTo;
    if (from >= _loadedFrom && to <= _loadedTo) return;

    var padding = (ulong)_viewport.VisibleDurationUs;
    _loadedFrom = from > padding ? from - padding : 0;
    _loadedTo = to + padding;
    vm.SetVisibleRange(_loadedFrom, _loadedTo);
  }

  private void ResetBrushes()
  {
    _spanBrush = null;
    _markerBrush = null;
    _tickBrush = null;
    _tickTextBrush = null;
    _playheadBrush = null;
    InvalidateTimeline();
  }

  private ISolidColorBrush SpanBrush =>
    _spanBrush ??= TryFindBrush("SpanRecordingBrush") ?? Brushes.CornflowerBlue;
  private ISolidColorBrush MarkerBrush =>
    _markerBrush ??= TryFindBrush("DangerBrush") ?? Brushes.Red;
  private ISolidColorBrush TickBrush =>
    _tickBrush ??= TryFindBrush("BorderBrush") ?? Brushes.Gray;
  private ISolidColorBrush TickTextBrush =>
    _tickTextBrush ??= TryFindBrush("TextMutedBrush") ?? Brushes.Gray;
  private ISolidColorBrush PlayheadBrush =>
    _playheadBrush ??= TryFindBrush("TextBrush") ?? Brushes.White;

  private ISolidColorBrush? TryFindBrush(string key)
  {
    if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var res) == true)
      return res as ISolidColorBrush;
    return null;
  }
}
