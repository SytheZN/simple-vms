using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Client.Core.Decoding.Renderers;

public sealed class SkiaFrameRenderer : IFrameRenderer, IDisposable
{
  private readonly ILogger _logger;
  private readonly Lock _frameLock = new();
  private DecodedFrame? _frame;
  private CompositionCustomVisual? _customVisual;
  private FrameHandler? _handler;
  private Visual? _host;
  private int _renderCount;
  private bool _disposed;

  public event Action? OnVsync;
  public string DisplayName => "SW Skia Render";

  public SkiaFrameRenderer(ILogger<SkiaFrameRenderer> logger)
  {
    _logger = logger;
  }

  public void Attach(Visual host)
  {
    if (_customVisual != null) return;
    _host = host;
    var compositor = ElementComposition.GetElementVisual(host)?.Compositor;
    _logger.LogDebug("SkiaFrameRenderer.Attach compositor={HasComp}", compositor != null);
    if (compositor == null) return;

    _handler = new FrameHandler(this);
    _customVisual = compositor.CreateCustomVisual(_handler);
    _customVisual.Size = new Vector(host.Bounds.Width, host.Bounds.Height);
    ElementComposition.SetElementChildVisual(host, _customVisual);
    _customVisual.SendHandlerMessage(FrameHandler.StartMessage);
    _logger.LogDebug("SkiaFrameRenderer.Attach customVisual created, size={W}x{H}",
      host.Bounds.Width, host.Bounds.Height);
  }

  public void Detach()
  {
    if (_host != null && _customVisual != null)
      ElementComposition.SetElementChildVisual(_host, null);
    _customVisual = null;
    _handler = null;
    _host = null;
  }

  public void Resize(Size size)
  {
    if (_customVisual != null)
      _customVisual.Size = new Vector(size.Width, size.Height);
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
    InvalidateOnUiThread();
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
    InvalidateOnUiThread();
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    Clear();
    Detach();
  }

  private void InvalidateOnUiThread()
  {
    var visual = _customVisual;
    if (visual == null) return;
    if (Dispatcher.UIThread.CheckAccess())
      visual.SendHandlerMessage(FrameHandler.InvalidateMessage);
    else
      Dispatcher.UIThread.Post(() =>
        _customVisual?.SendHandlerMessage(FrameHandler.InvalidateMessage));
  }

  private DecodedFrame? Snapshot()
  {
    lock (_frameLock)
    {
      _frame?.IncrementRef();
      return _frame;
    }
  }

  private void RaiseVsync() => OnVsync?.Invoke();

  private void TraceRender(bool hasFrame)
  {
    if (_logger.IsEnabled(LogLevel.Trace) && _renderCount++ % 60 == 0)
      _logger.LogTrace("SkiaFrameRenderer.OnRender #{N} hasFrame={HasFrame}",
        _renderCount, hasFrame);
  }

  private sealed class FrameHandler : CompositionCustomVisualHandler
  {
    public static readonly object StartMessage = new();
    public static readonly object InvalidateMessage = new();

    private readonly SkiaFrameRenderer _renderer;

    public FrameHandler(SkiaFrameRenderer renderer) { _renderer = renderer; }

    public override void OnMessage(object message)
    {
      if (ReferenceEquals(message, StartMessage))
        RegisterForNextAnimationFrameUpdate();
      else if (ReferenceEquals(message, InvalidateMessage))
        Invalidate();
    }

    public override void OnAnimationFrameUpdate()
    {
      _renderer.RaiseVsync();
      Invalidate();
      RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
      using var frame = _renderer.Snapshot();
      _renderer.TraceRender(frame != null);
      if (frame is not CpuDecodedFrame cpu || cpu.Pixels == 0 || cpu.Width == 0) return;

      if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature)
        return;

      using var lease = feature.Lease();
      var info = new SKImageInfo(cpu.Width, cpu.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
      using var image = SKImage.FromPixels(info, cpu.Pixels, cpu.Stride);
      if (image == null) return;

      var size = EffectiveSize;
      var src = new SKRect(0, 0, cpu.Width, cpu.Height);
      var dst = new SKRect(0, 0, (float)size.X, (float)size.Y);
      var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
      lease.SkCanvas.DrawImage(image, src, dst, sampling);
    }
  }
}
