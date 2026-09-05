using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Core.Decoding.Diagnostics;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public sealed class PlaybackStatsOverlay : Control
{
  public static readonly StyledProperty<PlaybackDiagnostics?> DiagnosticsProperty =
    AvaloniaProperty.Register<PlaybackStatsOverlay, PlaybackDiagnostics?>(nameof(Diagnostics));

  public PlaybackDiagnostics? Diagnostics
  {
    get => GetValue(DiagnosticsProperty);
    set => SetValue(DiagnosticsProperty, value);
  }

  public Func<PipelineStats[]>? PipelineStatsPull { get; set; }

  private const double Width_ = 340;
  private const double GraphHeight = 64;
  private const double LineHeight = 13;
  private const double HalfGap = 6.5;
  private const double Padding = 8;

  private static Typeface? _monoFace;
  private static Typeface MonoFace => _monoFace ??= new Typeface(new FontFamily(
    "avares://Client.Core/Assets/Monaspace/MonaspaceNeonFrozen-Regular.ttf#Monaspace Neon Frozen"));
  private static readonly SolidColorBrush BgBrush = new(Color.FromArgb(180, 0, 0, 0));
  private static readonly SolidColorBrush TextBrush = new(Color.FromArgb(230, 230, 230, 230));
  private static readonly SolidColorBrush AxisBrush = new(Color.FromArgb(90, 255, 255, 255));
  private static readonly SolidColorBrush OkBrush = new(Color.FromArgb(220, 80, 200, 120));
  private static readonly SolidColorBrush WarnBrush = new(Color.FromArgb(220, 220, 190, 80));
  private static readonly SolidColorBrush BadBrush = new(Color.FromArgb(220, 220, 90, 80));

  private DispatcherTimer? _timer;
  private readonly double[] _samples = new double[FrameTimingRecorder.Capacity];
  private int _lastPipelineCount = -1;

  protected override Size MeasureOverride(Size _)
  {
    var pipelineCount = PipelineStatsPull != null ? PipelineStatsPull().Length : 1;
    var textHeight = 2 * LineHeight + HalfGap
                     + pipelineCount * (3 * LineHeight + HalfGap)
                     + 2 * LineHeight + HalfGap;
    return new(Width_, textHeight + GraphHeight + Padding * 2);
  }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnAttachedToVisualTree(e);
    _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
    _timer.Tick += (_, _) =>
    {
      var current = PipelineStatsPull?.Invoke().Length ?? 1;
      if (current != _lastPipelineCount)
      {
        _lastPipelineCount = current;
        InvalidateMeasure();
      }
      InvalidateVisual();
    };
    _timer.Start();
  }

  protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnDetachedFromVisualTree(e);
    _timer?.Stop();
    _timer = null;
  }

  public override void Render(DrawingContext dc)
  {
    var d = Diagnostics;
    if (d == null || !d.Enabled) return;
    var s = d.Snapshot();
    d.FrameTiming.CopyMs(_samples);

    var pipelines = PipelineStatsPull?.Invoke() ?? [];

    var pipelineCount = Math.Max(pipelines.Length, 1);
    var textHeight = 2 * LineHeight + HalfGap
                     + pipelineCount * (3 * LineHeight + HalfGap)
                     + 2 * LineHeight + HalfGap;
    var w = Bounds.Width > 0 ? Bounds.Width : Width_;
    var totalHeight = textHeight + GraphHeight + Padding * 2;

    dc.FillRectangle(BgBrush, new Rect(0, 0, w, totalHeight), 4);

    double last = 0, sum = 0, min = double.MaxValue, max = 0;
    var count = d.FrameTiming.SampleCount;
    for (var i = 0; i < count; i++)
    {
      var v = _samples[FrameTimingRecorder.Capacity - count + i];
      sum += v;
      if (v < min) min = v;
      if (v > max) max = v;
    }
    if (count > 0) last = _samples[FrameTimingRecorder.Capacity - 1];
    var avg = count > 0 ? sum / count : 0;
    if (count == 0) min = 0;

    var curFps = last > 0 ? 1000.0 / last : 0;
    var avgFps = sum > 0 ? 1000.0 * count / sum : 0;
    double sumSec = 0;
    var secCount = 0;
    for (var i = FrameTimingRecorder.Capacity - 1; i >= 0; i--)
    {
      var v = _samples[i];
      if (v <= 0) break;
      sumSec += v;
      secCount++;
      if (sumSec >= 1000) break;
    }
    var secFps = sumSec > 0 ? 1000.0 * secCount / sumSec : 0;

    var c = CultureInfo.InvariantCulture;
    var y = Padding;

    DrawLine(dc, ref y, $"{s.BackendName} / {s.RendererName}");
    DrawLine(dc, ref y, string.Create(c,
      $"{s.Mode}/{s.State}  {s.Rate:0.00}x  catchup {s.CatchupRate:0.000}x{(s.Buffering ? "  BUFFERING" : "")}"));
    y += HalfGap;

    foreach (var p in pipelines)
    {
      DrawLine(dc, ref y, string.Create(c, $"{p.Label}  {p.Profile}"));
      DrawLine(dc, ref y, string.Create(c,
        $"decode buf {p.BufferUs / 1000.0,5:0}ms  pos {FormatTs(p.PositionUs)}"));
      DrawLine(dc, ref y, string.Create(c,
        $"fetch  {p.FetcherGops} GOPs  {p.FetcherBytes / 1024.0 / 1024.0:0.0}MiB    decode  {p.DecoderGops} GOPs  {p.DecoderFrames} fr"));
      y += HalfGap;
    }

    DrawLine(dc, ref y, string.Create(c,
      $"dt  last {last,4:0}ms  avg {avg,4:0}ms  min {min,4:0}ms  max {max,4:0}ms"));
    DrawLine(dc, ref y, string.Create(c,
      $"fps  cur {curFps,6:0.00}   1s {secFps,6:0.00}  avg {avgFps,6:0.00}"));
    y += HalfGap;

    var gw = w - Padding * 2;
    dc.FillRectangle(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
      new Rect(Padding, y, gw, GraphHeight));

    if (count > 0)
    {
      const double LabelReserve = 6;
      var barArea = GraphHeight - LabelReserve;
      var scale = Math.Max(max * 1.15, 50);
      var barW = gw / FrameTimingRecorder.Capacity;
      for (var i = 0; i < FrameTimingRecorder.Capacity; i++)
      {
        var v = _samples[i];
        if (v <= 0) continue;
        var barH = Math.Min(barArea, v / scale * barArea);
        var brush = v <= 45 ? OkBrush : v <= 80 ? WarnBrush : BadBrush;
        dc.FillRectangle(brush, new Rect(Padding + i * barW, y + GraphHeight - barH, Math.Max(1, barW - 0.5), barH));
      }

      var scaleLabel = new FormattedText(
        $"{scale:0}ms",
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        MonoFace,
        9,
        AxisBrush);
      dc.DrawText(scaleLabel, new Point(Padding + 2, y + 1));
    }
  }

  private void DrawLine(DrawingContext dc, ref double y, string text)
  {
    var ft = new FormattedText(
      text,
      CultureInfo.InvariantCulture,
      FlowDirection.LeftToRight,
      MonoFace,
      10,
      TextBrush);
    dc.DrawText(ft, new Point(Padding, y));
    y += LineHeight;
  }

  private static string FormatTs(long us)
  {
    if (us <= 0) return "--";
    var dt = DateTimeOffset.FromUnixTimeMilliseconds(us / 1000).ToLocalTime();
    return dt.ToString("HH:mm:ss.fff");
  }
}
