using Server.Core;

namespace Tests.Unit.Mocks;

public sealed class FakeCameraPauseState : ICameraPauseState
{
  public HashSet<Guid> Paused { get; } = [];

  public bool IsPaused(Guid cameraId) => Paused.Contains(cameraId);

  public ulong? GetPausedUntil(Guid cameraId) => Paused.Contains(cameraId) ? ulong.MaxValue : null;
}
