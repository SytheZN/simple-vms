using Client.Core.ViewModels;

namespace Client.Core.Streaming;

public interface IGalleryThumbnails
{
  void Sync(IReadOnlyList<CameraTile> tiles);
  void Stop();
}
