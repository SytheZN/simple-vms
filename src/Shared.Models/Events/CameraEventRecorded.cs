namespace Shared.Models.Events;

/// <summary>
/// Raised once an event has been written to history, carrying the row as written so that a client
/// shown this event finds the same one when it later queries.
/// </summary>
public sealed class CameraEventRecorded : ISystemEvent
{
  public required Guid Id { get; init; }
  public required Guid CameraId { get; init; }
  public required string Type { get; init; }
  public required ulong Timestamp { get; init; }
  public ulong? EndTime { get; init; }
  public Dictionary<string, string>? Metadata { get; init; }

  /// <summary>
  /// Set when the row closes a duration event rather than opening one.
  /// </summary>
  public bool Ended { get; init; }
}
