using Avalonia;
using Avalonia.Controls;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public sealed class AspectRatioPanel : Panel
{
  public static readonly StyledProperty<double> RatioProperty =
    AvaloniaProperty.Register<AspectRatioPanel, double>(nameof(Ratio), 16.0 / 9.0);

  public double Ratio
  {
    get => GetValue(RatioProperty);
    set => SetValue(RatioProperty, value);
  }

  protected override Size MeasureOverride(Size availableSize)
  {
    var size = Fit(availableSize);
    foreach (var child in Children) child.Measure(size);
    return size;
  }

  protected override Size ArrangeOverride(Size finalSize)
  {
    var size = Fit(finalSize);
    var x = (finalSize.Width - size.Width) * 0.5;
    var y = (finalSize.Height - size.Height) * 0.5;
    var rect = new Rect(x, y, size.Width, size.Height);
    foreach (var child in Children) child.Arrange(rect);
    return finalSize;
  }

  private Size Fit(Size available)
  {
    var w = available.Width;
    var h = available.Height;
    if (double.IsInfinity(w) && double.IsInfinity(h)) return new Size();
    if (double.IsInfinity(w)) return new Size(h * Ratio, h);
    if (double.IsInfinity(h)) return new Size(w, w / Ratio);
    return h * Ratio <= w ? new Size(h * Ratio, h) : new Size(w, w / Ratio);
  }
}
