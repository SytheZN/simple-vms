using Avalonia;

namespace Client.Core.Decoding;

public interface IFrameRenderer : IDisposable
{
  string DisplayName { get; }

  void RenderFrame(DecodedFrame frame);

  void Clear();

  void Attach(Visual host);

  void Detach();

  void Resize(Size size);

  event Action? OnVsync;
}
