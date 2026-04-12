using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Styling;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public sealed class Spinner : Ellipse
{
  public static readonly StyledProperty<double> SizeProperty =
    AvaloniaProperty.Register<Spinner, double>(nameof(Size), 24);

  public double Size
  {
    get => GetValue(SizeProperty);
    set => SetValue(SizeProperty, value);
  }

  public Spinner()
  {
    StrokeThickness = 2;
    Fill = Brushes.Transparent;
    StrokeDashArray = [3, 2];
    RenderTransformOrigin = RelativePoint.Center;
    RenderTransform = new RotateTransform(0);

    var animation = new Animation
    {
      Duration = TimeSpan.FromSeconds(1),
      IterationCount = IterationCount.Infinite,
      Children =
      {
        new KeyFrame
        {
          Cue = new Cue(0),
          Setters = { new Setter(RotateTransform.AngleProperty, 0.0) }
        },
        new KeyFrame
        {
          Cue = new Cue(1),
          Setters = { new Setter(RotateTransform.AngleProperty, 360.0) }
        }
      }
    };
    animation.RunAsync(this, default);
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);
    if (change.Property == SizeProperty)
    {
      Width = change.GetNewValue<double>();
      Height = change.GetNewValue<double>();
    }
  }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnAttachedToVisualTree(e);
    Width = Size;
    Height = Size;
    if (Stroke == null &&
        Application.Current?.TryGetResource("PrimaryBrush", Application.Current.ActualThemeVariant, out var res) == true &&
        res is IBrush brush)
      Stroke = brush;
  }
}
