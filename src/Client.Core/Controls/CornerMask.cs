using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public sealed class CornerMask : Control
{
  public static readonly StyledProperty<double> RadiusProperty =
    AvaloniaProperty.Register<CornerMask, double>(nameof(Radius), 12);

  public static readonly StyledProperty<IBrush?> FillProperty =
    AvaloniaProperty.Register<CornerMask, IBrush?>(nameof(Fill));

  public static readonly StyledProperty<bool> TopLeftProperty =
    AvaloniaProperty.Register<CornerMask, bool>(nameof(TopLeft), true);

  public static readonly StyledProperty<bool> TopRightProperty =
    AvaloniaProperty.Register<CornerMask, bool>(nameof(TopRight), true);

  public static readonly StyledProperty<bool> BottomLeftProperty =
    AvaloniaProperty.Register<CornerMask, bool>(nameof(BottomLeft));

  public static readonly StyledProperty<bool> BottomRightProperty =
    AvaloniaProperty.Register<CornerMask, bool>(nameof(BottomRight));

  public double Radius
  {
    get => GetValue(RadiusProperty);
    set => SetValue(RadiusProperty, value);
  }

  public IBrush? Fill
  {
    get => GetValue(FillProperty);
    set => SetValue(FillProperty, value);
  }

  public bool TopLeft
  {
    get => GetValue(TopLeftProperty);
    set => SetValue(TopLeftProperty, value);
  }

  public bool TopRight
  {
    get => GetValue(TopRightProperty);
    set => SetValue(TopRightProperty, value);
  }

  public bool BottomLeft
  {
    get => GetValue(BottomLeftProperty);
    set => SetValue(BottomLeftProperty, value);
  }

  public bool BottomRight
  {
    get => GetValue(BottomRightProperty);
    set => SetValue(BottomRightProperty, value);
  }

  public override void Render(DrawingContext context)
  {
    var fill = Fill;
    if (fill == null) return;

    var r = Radius;
    var w = Bounds.Width;
    var h = Bounds.Height;

    if (TopLeft)
      DrawCorner(context, fill, 0, 0, r, false, false);
    if (TopRight)
      DrawCorner(context, fill, w - r, 0, r, true, false);
    if (BottomLeft)
      DrawCorner(context, fill, 0, h - r, r, false, true);
    if (BottomRight)
      DrawCorner(context, fill, w - r, h - r, r, true, true);
  }

  private static void DrawCorner(DrawingContext ctx, IBrush fill,
    double x, double y, double r, bool flipX, bool flipY)
  {
    var geo = new StreamGeometry();
    using var gc = geo.Open();

    var ox = flipX ? x + r : x;
    var oy = flipY ? y + r : y;

    gc.BeginFigure(new Point(ox, oy), true);
    gc.LineTo(new Point(flipX ? x : x + r, oy));
    gc.ArcTo(
      new Point(ox, flipY ? y : y + r),
      new Size(r, r),
      0,
      false,
      flipX == flipY ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
    gc.EndFigure(true);

    ctx.DrawGeometry(fill, null, geo);
  }
}
