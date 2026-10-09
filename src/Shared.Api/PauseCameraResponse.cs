namespace Shared.Api;

public sealed class PauseCameraResponse
{
  public required ulong? PausedUntil { get; init; }
}
