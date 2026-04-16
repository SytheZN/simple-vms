namespace Client.Core.Decoding;

/// <summary>
/// Surface that draws decoded frames. Port of the web CanvasRenderer role -
/// the Player calls RenderFrame with a borrowed DecodedFrame every time a
/// new frame is selected, and the renderer manages its own refcount to keep
/// pixels alive until it next replaces the frame or detaches.
/// </summary>
public interface IFrameRenderer
{
  /// <summary>
  /// Draw (or queue for the next vsync) the given frame. The frame is borrowed:
  /// the renderer must IncrementRef to retain it and Dispose the prior frame.
  /// </summary>
  void RenderFrame(DecodedFrame frame);

  /// <summary>
  /// Drop any retained frame reference. Called on detach.
  /// </summary>
  void Clear();
}
