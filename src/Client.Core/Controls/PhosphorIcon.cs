using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Platform;

namespace Client.Core.Controls;

/// <summary>
/// Names map to Phosphor icon file slugs (kebab-case on disk). Loaded from
/// src/Client.Core/Assets/Phosphor/ as AvaloniaResource SVGs.
/// </summary>
public enum PhosphorIconKind
{
  ArrowLeft,
  ArrowsIn,
  ArrowsOut,
  CaretLeft,
  CaretRight,
  Copy,
  CornersIn,
  CornersOut,
  Eye,
  EyeSlash,
  Funnel,
  Gear,
  Keyboard,
  Lightning,
  Minus,
  Pause,
  PersonArmsSpread,
  Play,
  Plus,
  ShieldCheck,
  ShieldWarning,
  SignOut,
  SkipForward,
  SquaresFour,
  VideoCamera,
  VideoCameraSlash,
  Warning,
  WifiSlash,
  X,
  XCircle
}

/// <summary>
/// Renders a single Phosphor icon from its SVG resource. The SVG's 256x256
/// viewBox is scaled uniformly to the control's Bounds; shapes are stroked
/// in Foreground at Phosphor's canonical 16-unit thickness with round caps
/// and joins.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class PhosphorIcon : Control
{
  public static readonly StyledProperty<PhosphorIconKind> KindProperty =
    AvaloniaProperty.Register<PhosphorIcon, PhosphorIconKind>(nameof(Kind));

  public static readonly StyledProperty<IBrush?> ForegroundProperty =
    TextElement.ForegroundProperty.AddOwner<PhosphorIcon>();

  public PhosphorIconKind Kind
  {
    get => GetValue(KindProperty);
    set => SetValue(KindProperty, value);
  }

  public IBrush? Foreground
  {
    get => GetValue(ForegroundProperty);
    set => SetValue(ForegroundProperty, value);
  }

  static PhosphorIcon()
  {
    AffectsRender<PhosphorIcon>(KindProperty, ForegroundProperty);
  }

  public override void Render(DrawingContext context)
  {
    if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
    var geometry = PhosphorIconData.GetGeometry(Kind);
    if (geometry == null) return;

    var brush = Foreground ?? Brushes.Black;
    var pen = new Pen(brush, PhosphorIconData.StrokeWidth,
      lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    var scale = Math.Min(Bounds.Width, Bounds.Height) / ViewBoxSize;
    var offsetX = (Bounds.Width - ViewBoxSize * scale) * 0.5;
    var offsetY = (Bounds.Height - ViewBoxSize * scale) * 0.5;

    using (context.PushTransform(Matrix.CreateScale(scale, scale)
      * Matrix.CreateTranslation(offsetX, offsetY)))
      context.DrawGeometry(null, pen, geometry);
  }

  private const double ViewBoxSize = 256;
}

internal static class PhosphorIconData
{
  internal const double StrokeWidth = 16;

  private static readonly Dictionary<PhosphorIconKind, Geometry?> _cache = [];
  private static readonly Lock _cacheLock = new();

  public static Geometry? GetGeometry(PhosphorIconKind kind)
  {
    lock (_cacheLock)
    {
      if (_cache.TryGetValue(kind, out var cached)) return cached;
      var geom = Load(kind);
      _cache[kind] = geom;
      return geom;
    }
  }

  private static Geometry? Load(PhosphorIconKind kind)
  {
    var slug = KindToSlug(kind);
    var uri = new Uri($"avares://Client.Core/Assets/Phosphor/{slug}.svg");
    using var stream = AssetLoader.Open(uri);
    var doc = XDocument.Load(stream);
    var svgNs = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

    var group = new GeometryGroup { FillRule = FillRule.NonZero };

    foreach (var el in doc.Descendants())
    {
      if (el.Name.Namespace != svgNs) continue;
      var stroke = (string?)el.Attribute("stroke");
      // Skip decorative <rect width="256" height="256" fill="none"/> backgrounds
      // that carry no stroke.
      if (el.Name.LocalName == "rect" && stroke == null) continue;
      var g = ParseElement(el);
      if (g != null) group.Children.Add(g);
    }
    return group;
  }

  private static Geometry? ParseElement(XElement el) =>
    el.Name.LocalName switch
    {
      "path" => StreamGeometry.Parse((string?)el.Attribute("d") ?? ""),
      "line" => LineGeometry(el),
      "polyline" => PolylineGeometry(el, closed: false),
      "polygon" => PolylineGeometry(el, closed: true),
      "rect" => RectGeometry(el),
      "circle" => CircleGeometry(el),
      _ => null
    };

  private static Geometry LineGeometry(XElement el)
  {
    var x1 = A(el, "x1");
    var y1 = A(el, "y1");
    var x2 = A(el, "x2");
    var y2 = A(el, "y2");
    return new LineGeometry(new Point(x1, y1), new Point(x2, y2));
  }

  private static Geometry PolylineGeometry(XElement el, bool closed)
  {
    var points = ParsePoints((string?)el.Attribute("points") ?? "");
    var figure = new PathFigure
    {
      StartPoint = points.Count > 0 ? points[0] : new Point(0, 0),
      IsClosed = closed,
      IsFilled = false
    };
    for (var i = 1; i < points.Count; i++)
      figure.Segments!.Add(new LineSegment { Point = points[i] });
    var path = new PathGeometry();
    path.Figures!.Add(figure);
    return path;
  }

  private static Geometry RectGeometry(XElement el)
  {
    var x = A(el, "x");
    var y = A(el, "y");
    var w = A(el, "width");
    var h = A(el, "height");
    var rx = A(el, "rx");
    var ry = (double?)el.Attribute("ry") ?? rx;
    if (rx <= 0 && ry <= 0)
      return new RectangleGeometry(new Rect(x, y, w, h));
    return new RectangleGeometry(new Rect(x, y, w, h), rx, ry);
  }

  private static Geometry CircleGeometry(XElement el)
  {
    var cx = A(el, "cx");
    var cy = A(el, "cy");
    var r = A(el, "r");
    return new EllipseGeometry(new Rect(cx - r, cy - r, r * 2, r * 2));
  }

  private static List<Point> ParsePoints(string raw)
  {
    var tokens = raw.Split([' ', ',', '\t', '\n', '\r'],
      StringSplitOptions.RemoveEmptyEntries);
    var list = new List<Point>(tokens.Length / 2);
    for (var i = 0; i + 1 < tokens.Length; i += 2)
    {
      var x = double.Parse(tokens[i], CultureInfo.InvariantCulture);
      var y = double.Parse(tokens[i + 1], CultureInfo.InvariantCulture);
      list.Add(new Point(x, y));
    }
    return list;
  }

  private static double A(XElement el, string name)
  {
    var s = (string?)el.Attribute(name);
    return s == null ? 0 : double.Parse(s, CultureInfo.InvariantCulture);
  }

  private static string KindToSlug(PhosphorIconKind kind) =>
    kind switch
    {
      PhosphorIconKind.ArrowLeft => "arrow-left",
      PhosphorIconKind.ArrowsIn => "arrows-in",
      PhosphorIconKind.ArrowsOut => "arrows-out",
      PhosphorIconKind.CaretLeft => "caret-left",
      PhosphorIconKind.CaretRight => "caret-right",
      PhosphorIconKind.Copy => "copy",
      PhosphorIconKind.CornersIn => "corners-in",
      PhosphorIconKind.CornersOut => "corners-out",
      PhosphorIconKind.Eye => "eye",
      PhosphorIconKind.EyeSlash => "eye-slash",
      PhosphorIconKind.Funnel => "funnel",
      PhosphorIconKind.Gear => "gear",
      PhosphorIconKind.Keyboard => "keyboard",
      PhosphorIconKind.Lightning => "lightning",
      PhosphorIconKind.Minus => "minus",
      PhosphorIconKind.Pause => "pause",
      PhosphorIconKind.PersonArmsSpread => "person-arms-spread",
      PhosphorIconKind.Play => "play",
      PhosphorIconKind.Plus => "plus",
      PhosphorIconKind.ShieldCheck => "shield-check",
      PhosphorIconKind.ShieldWarning => "shield-warning",
      PhosphorIconKind.SignOut => "sign-out",
      PhosphorIconKind.SkipForward => "skip-forward",
      PhosphorIconKind.SquaresFour => "squares-four",
      PhosphorIconKind.VideoCamera => "video-camera",
      PhosphorIconKind.VideoCameraSlash => "video-camera-slash",
      PhosphorIconKind.Warning => "warning",
      PhosphorIconKind.WifiSlash => "wifi-slash",
      PhosphorIconKind.X => "x",
      PhosphorIconKind.XCircle => "x-circle",
      _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
