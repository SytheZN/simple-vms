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
    var width = availableSize.Width;
    var height = double.IsInfinity(width) ? 0 : width / Ratio;
    var childSize = new Size(width, height);

    foreach (var child in Children)
      child.Measure(childSize);

    return childSize;
  }

  protected override Size ArrangeOverride(Size finalSize)
  {
    var height = finalSize.Width / Ratio;
    var size = new Size(finalSize.Width, height);
    var rect = new Rect(size);

    foreach (var child in Children)
      child.Arrange(rect);

    return size;
  }
}
