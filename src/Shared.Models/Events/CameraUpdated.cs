namespace Shared.Models.Events;

public sealed class CameraUpdated : ISystemEvent
{
  public required Guid CameraId { get; init; }
  public required string Name { get; init; }
  public string? PreviousName { get; init; }
  public string? Address { get; init; }
  public string? ProviderId { get; init; }
  public bool CredentialsUpdated { get; init; }
  public string? RtspPortOverride { get; init; }
  public required ulong Timestamp { get; init; }
}
