using Avalonia;

namespace Client.Core.Decoding;

public interface IFrameRenderer : IDisposable
{
  string DisplayName { get; }

  /// <summary>
  /// The frame is borrowed: the renderer IncrementRefs to retain it and
  /// Disposes the prior frame.
  /// </summary>
  void RenderFrame(DecodedFrame frame);

  void Clear();

  /// <summary>Required before RenderFrame.</summary>
  void Attach(Visual host);

  /// <summary>Does not release a retained frame; call Clear separately.</summary>
  void Detach();

  void Resize(Size size);

  /// <summary>Raised on the compositor thread; host subscribes to drive Player.Tick.</summary>
  event Action? OnVsync;
}
