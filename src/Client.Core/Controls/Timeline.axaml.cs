using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Client.Core.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class Timeline : UserControl
{
  public static readonly StyledProperty<TimelineViewModel?> ViewModelProperty =
    AvaloniaProperty.Register<Timeline, TimelineViewModel?>(nameof(ViewModel));

  private static readonly int[] HourIntervals = [1, 2, 5, 10, 15, 30, 60, 120, 240, 360, 720];
  private static readonly int[] DayIntervals = [1, 2, 3, 7];
  private const int MaxTicks = 8;

  private ISolidColorBrush SpanBrush => TryFindBrush("SpanRecordingBrush") ?? Brushes.CornflowerBlue;
  private ISolidColorBrush MarkerBrush => TryFindBrush("DangerBrush") ?? Brushes.Red;
  private ISolidColorBrush TickBrush => TryFindBrush("BorderBrush") ?? Brushes.Gray;
  private ISolidColorBrush TickTextBrush => TryFindBrush("TextMutedBrush") ?? Brushes.Gray;

  private readonly Canvas _spanCanvas;
  private readonly Canvas _markerCanvas;
  private readonly Canvas _tickCanvas;
  private readonly Border _playhead;
  private bool _isDragging;
  private double? _pinchStartScale;

  public TimelineViewModel? ViewModel
  {
    get => GetValue(ViewModelProperty);
    set => SetValue(ViewModelProperty, value);
  }

  public event Action<ulong>? Scrubbed;

  static Timeline()
  {
    Gestures.PinchEvent.AddClassHandler<Timeline>((t, e) => t.OnPinch(e));
    Gestures.PinchEndedEvent.AddClassHandler<Timeline>((t, e) => t.OnPinchEnded(e));
  }

  public Timeline()
  {
    InitializeComponent();
    _spanCanvas = this.FindControl<Canvas>("SpanCanvas")!;
    _markerCanvas = this.FindControl<Canvas>("MarkerCanvas")!;
    _tickCanvas = this.FindControl<Canvas>("TickCanvas")!;
    _playhead = this.FindControl<Border>("Playhead")!;

    _playhead.PointerPressed += OnPlayheadPressed;
    _playhead.PointerMoved += OnPlayheadMoved;
    _playhead.PointerReleased += OnPlayheadReleased;
    PointerWheelChanged += OnPointerWheelChanged;
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

    if (change.Property == ViewModelProperty)
    {
      var oldVm = change.GetOldValue<TimelineViewModel?>();
      if (oldVm != null)
        oldVm.PropertyChanged -= OnViewModelPropertyChanged;

      var newVm = change.GetNewValue<TimelineViewModel?>();
      if (newVm != null)
        newVm.PropertyChanged += OnViewModelPropertyChanged;

      Render();
    }
  }

  private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(TimelineViewModel.VisibleFrom)
        or nameof(TimelineViewModel.VisibleTo)
        or nameof(TimelineViewModel.Spans))
      Render();
    else if (e.PropertyName is nameof(TimelineViewModel.CurrentPosition))
      UpdatePlayhead();
  }

  private void UpdatePlayhead()
  {
    var vm = ViewModel;
    if (vm == null || Bounds.Width <= 0) return;
    var from = vm.VisibleFrom;
    var to = vm.VisibleTo;
    if (to <= from) return;
    var range = (double)(to - from);
    var playheadPos = ((double)vm.CurrentPosition - from) / range * Bounds.Width;
    _playhead.Margin = new Thickness(playheadPos, 0, 0, 0);
  }

  protected override void OnSizeChanged(SizeChangedEventArgs e)
  {
    base.OnSizeChanged(e);
    Render();
  }

  private void Render()
  {
    var vm = ViewModel;
    if (vm == null || Bounds.Width <= 0) return;

    var from = vm.VisibleFrom;
    var to = vm.VisibleTo;
    if (to <= from) return;

    var range = (double)(to - from);
    var width = Bounds.Width;

    var pixelCount = (int)Math.Ceiling(width);
    if (pixelCount <= 0) return;

    _spanCanvas.Children.Clear();
    RenderSpans(vm.Spans, from, range, width, pixelCount);

    _markerCanvas.Children.Clear();
    RenderMarkers(vm.Events, from, range, width, pixelCount);

    var playheadPos = ((double)vm.CurrentPosition - from) / range * width;
    _playhead.Margin = new Thickness(playheadPos, 0, 0, 0);

    RenderTicks(from, to, range, width);
  }

  private void RenderSpans(
    System.Collections.ObjectModel.ObservableCollection<Shared.Models.Dto.TimelineSpan> spans,
    ulong from, double range, double width, int pixelCount)
  {
    var filled = new bool[pixelCount];
    foreach (var span in spans)
    {
      var left = Math.Max(0, (int)(((double)span.StartTime - from) / range * width));
      var right = Math.Min(pixelCount - 1, (int)(((double)span.EndTime - from) / range * width));
      for (var i = left; i <= right; i++)
        filled[i] = true;
    }

    var runStart = -1;
    for (var i = 0; i <= pixelCount; i++)
    {
      var active = i < pixelCount && filled[i];
      if (active && runStart < 0)
        runStart = i;
      else if (!active && runStart >= 0)
      {
        var rect = new Rectangle { Fill = SpanBrush, Height = 16 };
        Canvas.SetLeft(rect, runStart);
        rect.Width = Math.Max(1, i - runStart);
        _spanCanvas.Children.Add(rect);
        runStart = -1;
      }
    }
  }

  private void RenderMarkers(
    System.Collections.ObjectModel.ObservableCollection<Shared.Models.Dto.TimelineEvent> events,
    ulong from, double range, double width, int pixelCount)
  {
    var marked = new bool[pixelCount];
    foreach (var evt in events)
    {
      var px = (int)(((double)evt.StartTime - from) / range * width);
      if (px >= 0 && px < pixelCount)
        marked[px] = true;
    }

    var runStart = -1;
    for (var i = 0; i <= pixelCount; i++)
    {
      var active = i < pixelCount && marked[i];
      if (active && runStart < 0)
        runStart = i;
      else if (!active && runStart >= 0)
      {
        var marker = new Rectangle
        {
          Fill = MarkerBrush,
          Width = Math.Max(2, i - runStart),
          Height = 16
        };
        Canvas.SetLeft(marker, runStart);
        _markerCanvas.Children.Add(marker);
        runStart = -1;
      }
    }
  }

  private void RenderTicks(ulong from, ulong to, double range, double width)
  {
    _tickCanvas.Children.Clear();

    var rangeMinutes = range / (60 * 1_000_000);
    int? intervalMinutes = null;
    foreach (var m in HourIntervals)
    {
      if (rangeMinutes / m <= MaxTicks) { intervalMinutes = m; break; }
    }

    if (intervalMinutes != null)
      RenderHourTicks(from, to, range, width, intervalMinutes.Value);
    else
      RenderDayTicks(from, to, range, width);
  }

  private void RenderHourTicks(ulong from, ulong to, double range, double width, int intervalMinutes)
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

    for (var tickUs = firstTick; tickUs <= (long)to; tickUs += intervalUs)
    {
      var pct = ((double)tickUs - from) / range;
      if (pct < 0.03 || pct > 0.97) continue;

      var x = pct * width;
      var dt = DateTimeOffset.FromUnixTimeMilliseconds(tickUs / 1000).LocalDateTime;

      var tickLine = new Line
      {
        StartPoint = new Point(x, 0),
        EndPoint = new Point(x, 4),
        Stroke = TickBrush,
        StrokeThickness = 1
      };
      _tickCanvas.Children.Add(tickLine);

      string label;
      if (crossesDate && dt.Hour == 0 && dt.Minute == 0)
        label = dt.ToString("MM/dd");
      else
        label = dt.ToString("HH:mm");

      var text = new TextBlock
      {
        Text = label,
        FontSize = 10,
        Foreground = TickTextBrush,
      };
      Canvas.SetLeft(text, x - 16);
      Canvas.SetTop(text, 5);
      _tickCanvas.Children.Add(text);
    }
  }

  private void RenderDayTicks(ulong from, ulong to, double range, double width)
  {
    var rangeDays = range / (1440.0 * 60 * 1_000_000);
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

    for (var i = firstStep; i <= lastStep; i++)
    {
      var tickMs = anchorMs + (long)i * stepMs;
      var tickUs = (ulong)(tickMs * 1000);
      var pct = ((double)tickUs - from) / range;
      if (pct < 0.03 || pct > 0.97) continue;

      var x = pct * width;
      var dt = DateTimeOffset.FromUnixTimeMilliseconds(tickMs).LocalDateTime;

      var tickLine = new Line
      {
        StartPoint = new Point(x, 0),
        EndPoint = new Point(x, 4),
        Stroke = TickBrush,
        StrokeThickness = 1
      };
      _tickCanvas.Children.Add(tickLine);

      var text = new TextBlock
      {
        Text = dt.ToString("MM/dd"),
        FontSize = 10,
        Foreground = TickTextBrush,
      };
      Canvas.SetLeft(text, x - 16);
      Canvas.SetTop(text, 5);
      _tickCanvas.Children.Add(text);
    }
  }

  private ISolidColorBrush? TryFindBrush(string key)
  {
    if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var res) == true)
      return res as ISolidColorBrush;
    return null;
  }

  private void OnPlayheadPressed(object? sender, PointerPressedEventArgs e)
  {
    _isDragging = true;
    e.Pointer.Capture(_playhead);
  }

  private void OnPlayheadMoved(object? sender, PointerEventArgs e)
  {
    if (!_isDragging || ViewModel == null) return;

    var pos = e.GetPosition(this);
    var fraction = Math.Clamp(pos.X / Bounds.Width, 0, 1);
    var range = ViewModel.VisibleTo - ViewModel.VisibleFrom;
    ViewModel.CurrentPosition = ViewModel.VisibleFrom + (ulong)(fraction * range);
    Render();
  }

  private void OnPlayheadReleased(object? sender, PointerReleasedEventArgs e)
  {
    if (!_isDragging) return;
    _isDragging = false;
    e.Pointer.Capture(null);
    if (ViewModel != null)
      Scrubbed?.Invoke(ViewModel.CurrentPosition);
  }

  private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
  {
    if (ViewModel == null) return;

    var factor = e.Delta.Y > 0 ? 0.8 : 1.25;
    ApplyZoom(factor, e.GetPosition(this).X / Bounds.Width);
    e.Handled = true;
  }

  private void OnPinch(PinchEventArgs e)
  {
    if (ViewModel == null) return;

    _pinchStartScale ??= ViewModel.ZoomLevel;

    var targetScale = _pinchStartScale.Value / e.Scale;
    var factor = targetScale / ViewModel.ZoomLevel;
    ApplyZoom(factor, 0.5);
  }

  private void OnPinchEnded(PinchEndedEventArgs e)
  {
    _pinchStartScale = null;
  }

  private void ApplyZoom(double factor, double anchorFraction)
  {
    var vm = ViewModel;
    if (vm == null) return;

    var range = (double)(vm.VisibleTo - vm.VisibleFrom);
    var newRange = range * factor;
    var anchor = vm.VisibleFrom + (ulong)(anchorFraction * range);
    var newFrom = anchor > (ulong)(anchorFraction * newRange)
      ? anchor - (ulong)(anchorFraction * newRange)
      : 0UL;
    var newTo = newFrom + (ulong)newRange;

    vm.SetVisibleRange(newFrom, newTo);
  }
}
