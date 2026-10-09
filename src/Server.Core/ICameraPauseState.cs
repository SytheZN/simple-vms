namespace Server.Core;

public interface ICameraPauseState
{
  bool IsPaused(Guid cameraId);
  ulong? GetPausedUntil(Guid cameraId);
}
