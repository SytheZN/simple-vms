using Client.Core.Streaming;
using Client.Core.ViewModels;

namespace Tests.Unit.Client.Mocks;

public sealed class FakeGalleryThumbnails : IGalleryThumbnails
{
  public List<IReadOnlyList<CameraTile>> Synced { get; } = [];
  public List<bool> VisibleChanges { get; } = [];
  public int StopCount { get; private set; }

  public void Sync(IReadOnlyList<CameraTile> tiles) => Synced.Add([.. tiles]);

  public void SetVisible(bool visible) => VisibleChanges.Add(visible);

  public void Stop() => StopCount++;
}
