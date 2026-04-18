using Avalonia;
using Client.Core.Decoding;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public sealed class VideoSurface : Avalonia.Controls.Control
{
  private Decoding.Player? _player;
  private IFrameRenderer? _renderer;

  public void SetPlayer(Decoding.Player? player)
  {
    if (ReferenceEquals(_player, player)) return;

    if (_renderer != null)
    {
      _renderer.OnVsync -= OnRendererVsync;
      _renderer.Detach();
      _renderer = null;
    }

    _player = player;
    if (player == null) return;

    _renderer = player.Renderer;
    _renderer.Attach(this);
    _renderer.OnVsync += OnRendererVsync;
  }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnAttachedToVisualTree(e);
    if (_player != null && _renderer == null)
    {
      _renderer = _player.Renderer;
      _renderer.Attach(this);
      _renderer.OnVsync += OnRendererVsync;
    }
  }

  protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
  {
    base.OnDetachedFromVisualTree(e);
    if (_renderer != null)
    {
      _renderer.OnVsync -= OnRendererVsync;
      _renderer.Detach();
      _renderer = null;
    }
  }

  protected override Size ArrangeOverride(Size finalSize)
  {
    _renderer?.Resize(finalSize);
    return base.ArrangeOverride(finalSize);
  }

  private void OnRendererVsync() => _player?.Tick();
}
