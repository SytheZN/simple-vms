using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Client.Core.ViewModels;
using Shared.Models.Dto;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class Timeline : UserControl
{
  public static readonly StyledProperty<TimelineViewModel?> ViewModelProperty =
    AvaloniaProperty.Register<Timeline, TimelineViewModel?>(nameof(ViewModel));

  private static readonly ISolidColorBrush SpanBrush = new SolidColorBrush(Color.FromArgb(77, 100, 120, 200));
  private static readonly ISolidColorBrush MarkerBrush = new SolidColorBrush(Color.FromArgb(200, 200, 60, 60));

  private readonly Canvas _spanCanvas;
  private readonly Canvas _markerCanvas;
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
        or nameof(TimelineViewModel.VisibleTo))
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

    _spanCanvas.Children.Clear();
    foreach (var span in vm.Spans)
    {
      var left = ((double)span.StartTime - from) / range * width;
      var right = ((double)span.EndTime - from) / range * width;
      var rect = new Rectangle
      {
        Fill = SpanBrush,
        Height = Bounds.Height
      };
      Canvas.SetLeft(rect, left);
      rect.Width = Math.Max(1, right - left);
      _spanCanvas.Children.Add(rect);
    }

    _markerCanvas.Children.Clear();
    foreach (var evt in vm.Events)
    {
      var pos = ((double)evt.StartTime - from) / range * width;
      var marker = new Rectangle
      {
        Fill = MarkerBrush,
        Width = 2,
        Height = Bounds.Height
      };
      Canvas.SetLeft(marker, pos);
      _markerCanvas.Children.Add(marker);
    }

    var playheadPos = ((double)vm.CurrentPosition - from) / range * width;
    _playhead.Margin = new Thickness(playheadPos, 0, 0, 0);
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
