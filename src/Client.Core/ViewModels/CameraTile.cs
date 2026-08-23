using Avalonia.Media.Imaging;
using Shared.Api;

namespace Client.Core.ViewModels;

/// <summary>
/// A camera as the gallery shows it. The thumbnail outlives any particular camera record, so a
/// refresh replaces what the list says about the camera without dropping the picture.
/// </summary>
public sealed class CameraTile : ViewModelBase
{
  private CameraDto _camera;
  private Bitmap? _thumbnail;

  public CameraTile(CameraDto camera)
  {
    _camera = camera;
  }

  public Guid Id => _camera.Id;

  public CameraDto Camera
  {
    get => _camera;
    set => SetProperty(ref _camera, value);
  }

  public Bitmap? Thumbnail
  {
    get => _thumbnail;
    set
    {
      var previous = _thumbnail;
      if (SetProperty(ref _thumbnail, value))
        previous?.Dispose();
    }
  }
}
