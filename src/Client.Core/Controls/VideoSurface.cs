using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using Client.Core.Decoding;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

/// <summary>
/// Avalonia control that presents decoded video frames at compositor vsync
/// via a CompositionCustomVisualHandler running on the render thread.
/// The Player pushes each new frame via <see cref="IFrameRenderer.RenderFrame"/>;
/// the surface retains it (refcounted) and the handler blits it in OnRender.
/// Port of src/Client.Web/src/media/canvasRenderer.ts.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class VideoSurface : Avalonia.Controls.Control, IFrameRenderer
{
  private Player? _player;
  private CompositionCustomVisual? _customVisual;
  private FrameHandler? _handler;

  private DecodedFrame? _frame;
  private readonly Lock _frameLock = new();

  /// <summary>
  /// Optional diagnostic logger. The owner sets this so the surface can emit
  /// trace messages through the application's configured logging pipeline.
  /// </summary>
  public ILogger? Logger { get; set; }
  private int _tickCount;
  private int _renderCount;

  public void SetPlayer(Player? player)
  {
    if (ReferenceEquals(_player, player)) return;
    _player?.DetachRenderer(this);
    _player = player;
    Logger = player?.RendererLogger;
    Logger?.LogDebug("VideoSurface.SetPlayer player={HasPlayer} customVisual={HasVisual}",
      player != null, _customVisual != null);
    EnsureVisualAttached();
    _player?.AttachRenderer(this);
  }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnAttachedToVisualTree(e);
    Logger?.LogDebug("VideoSurface.OnAttachedToVisualTree player={HasPlayer} customVisual={HasVisual}",
      _player != null, _customVisual != null);
    EnsureVisualAttached();
    _player?.AttachRenderer(this);
  }

  protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnDetachedFromVisualTree(e);
    _player?.DetachRenderer(this);
    if (_customVisual != null)
    {
      ElementComposition.SetElementChildVisual(this, null);
      _customVisual = null;
      _handler = null;
    }
  }

  protected override Size ArrangeOverride(Size finalSize)
  {
    if (_customVisual != null)
      _customVisual.Size = new Vector(finalSize.Width, finalSize.Height);
    return base.ArrangeOverride(finalSize);
  }

  public void RenderFrame(DecodedFrame frame)
  {
    frame.IncrementRef();
    DecodedFrame? old;
    lock (_frameLock)
    {
      old = _frame;
      _frame = frame;
    }
    old?.Dispose();
    InvalidateVisualOnUiThread();
  }

  public void Clear()
  {
    DecodedFrame? old;
    lock (_frameLock)
    {
      old = _frame;
      _frame = null;
    }
    old?.Dispose();
    InvalidateVisualOnUiThread();
  }

  private void InvalidateVisualOnUiThread()
  {
    var visual = _customVisual;
    if (visual == null) return;
    if (Dispatcher.UIThread.CheckAccess())
      visual.SendHandlerMessage(FrameHandler.InvalidateMessage);
    else
      Dispatcher.UIThread.Post(() =>
        _customVisual?.SendHandlerMessage(FrameHandler.InvalidateMessage));
  }

  /// <summary>
  /// Returns a fresh +1 reference to the currently retained frame. Caller on
  /// the render thread disposes after drawing.
  /// </summary>
  internal DecodedFrame? Snapshot()
  {
    lock (_frameLock)
    {
      _frame?.IncrementRef();
      return _frame;
    }
  }

  internal void DriveTick()
  {
    if (Logger?.IsEnabled(LogLevel.Trace) == true && _tickCount++ % 60 == 0)
      Logger.LogTrace("VideoSurface.DriveTick #{N} player={HasPlayer}",
        _tickCount, _player != null);
    _player?.Tick();
  }

  internal void TraceRender()
  {
    if (Logger?.IsEnabled(LogLevel.Trace) == true && _renderCount++ % 60 == 0)
      Logger.LogTrace("VideoSurface.OnRender #{N} hasFrame={HasFrame}",
        _renderCount, _frame != null);
  }

  private void EnsureVisualAttached()
  {
    if (_customVisual != null) return;
    var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
    Logger?.LogDebug("VideoSurface.EnsureVisualAttached compositor={HasComp}", compositor != null);
    if (compositor == null) return;

    _handler = new FrameHandler(this);
    _customVisual = compositor.CreateCustomVisual(_handler);
    _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
    ElementComposition.SetElementChildVisual(this, _customVisual);
    _customVisual.SendHandlerMessage(FrameHandler.StartMessage);
    Logger?.LogDebug("VideoSurface.EnsureVisualAttached customVisual created, size={W}x{H}",
      Bounds.Width, Bounds.Height);
  }

  private sealed class FrameHandler : CompositionCustomVisualHandler
  {
    public static readonly object StartMessage = new();
    public static readonly object InvalidateMessage = new();

    private readonly VideoSurface _surface;

    public FrameHandler(VideoSurface surface) { _surface = surface; }

    public override void OnMessage(object message)
    {
      if (ReferenceEquals(message, StartMessage))
        RegisterForNextAnimationFrameUpdate();
      else if (ReferenceEquals(message, InvalidateMessage))
        Invalidate();
    }

    public override void OnAnimationFrameUpdate()
    {
      _surface.DriveTick();
      Invalidate();
      RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
      _surface.TraceRender();
      using var frame = _surface.Snapshot();
      if (frame == null || frame.Pixels == 0 || frame.Width == 0) return;

      if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature)
        return;

      using var lease = feature.Lease();
      var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
      using var image = SKImage.FromPixels(info, frame.Pixels, frame.Stride);
      if (image == null) return;

      var size = EffectiveSize;
      var src = new SKRect(0, 0, frame.Width, frame.Height);
      var dst = new SKRect(0, 0, (float)size.X, (float)size.Y);
      var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
      lease.SkCanvas.DrawImage(image, src, dst, sampling);
    }
  }
}
