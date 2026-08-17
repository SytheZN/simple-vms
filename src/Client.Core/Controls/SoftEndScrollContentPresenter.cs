using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public class SoftEndScrollContentPresenter : ScrollContentPresenter
{
  public static readonly StyledProperty<bool> SoftEndsProperty =
    AvaloniaProperty.Register<SoftEndScrollContentPresenter, bool>(nameof(SoftEnds));

  public static readonly StyledProperty<double> PullProperty =
    AvaloniaProperty.Register<SoftEndScrollContentPresenter, double>(nameof(Pull));

  private const double MaxPullViewportFraction = 0.2;
  private const double MaxPullDip = 120;
  private static readonly TimeSpan ReleaseDuration = TimeSpan.FromMilliseconds(220);

  private readonly TranslateTransform _pullTransform = new();
  private CancellationTokenSource? _release;
  private double _rawPull;

  public bool SoftEnds
  {
    get => GetValue(SoftEndsProperty);
    set => SetValue(SoftEndsProperty, value);
  }

  public double Pull
  {
    get => GetValue(PullProperty);
    set => SetValue(PullProperty, value);
  }

  public SoftEndScrollContentPresenter()
  {
    AddHandler(ScrollGestureEvent, OnGesture, handledEventsToo: true);
    AddHandler(ScrollGestureEndedEvent, OnGestureEnded, handledEventsToo: true);
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);
    if (change.Property == PullProperty) ApplyPull();
  }

  private void OnGesture(object? sender, ScrollGestureEventArgs e)
  {
    if (!SoftEnds) return;

    if (e.Handled)
    {
      BeginRelease();
      return;
    }

    CancelRelease();
    _rawPull -= e.Delta.Y;
    SetCurrentValue(PullProperty, Resist(_rawPull));
    e.Handled = true;
    e.ShouldEndScrollGesture = true;
  }

  private void OnGestureEnded(object? sender, ScrollGestureEndedEventArgs e) => BeginRelease();

  private double Resist(double raw)
  {
    var limit = Math.Min(Viewport.Height * MaxPullViewportFraction, MaxPullDip);
    if (limit <= 0) return 0;
    var distance = Math.Abs(raw);
    return Math.Sign(raw) * limit * distance / (distance + limit);
  }

  private void ApplyPull()
  {
    if (Child is not { } child) return;
    if (!ReferenceEquals(child.RenderTransform, _pullTransform))
      child.RenderTransform = _pullTransform;
    _pullTransform.Y = Pull;
  }

  private void BeginRelease()
  {
    _rawPull = 0;
    if (Pull == 0 || _release != null) return;

    _release = new CancellationTokenSource();

    var animation = new Animation
    {
      Duration = ReleaseDuration,
      Easing = new CubicEaseOut(),
      FillMode = FillMode.Forward,
      Children =
      {
        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(PullProperty, Pull) } },
        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(PullProperty, 0d) } },
      },
    };

    _ = RunReleaseAsync(animation, _release.Token);
  }

  private async Task RunReleaseAsync(Animation animation, CancellationToken token)
  {
    await animation.RunAsync(this, token);
    if (!token.IsCancellationRequested) SetCurrentValue(PullProperty, 0d);
  }

  private void CancelRelease()
  {
    _release?.Cancel();
    _release?.Dispose();
    _release = null;
  }
}
