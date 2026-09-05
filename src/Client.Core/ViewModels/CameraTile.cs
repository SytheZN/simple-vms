using Avalonia.Media.Imaging;
using Shared.Api;

namespace Client.Core.ViewModels;

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
