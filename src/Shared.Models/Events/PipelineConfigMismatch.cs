namespace Shared.Models.Events;

/// <summary>
/// Raised by a capture source when the stream it is asked to resume no longer matches the one it
/// was originally probed for. Identified by URI because a capture source knows nothing of cameras.
/// </summary>
public sealed class PipelineConfigMismatch : ISystemEvent
{
  public required string Uri { get; init; }
  public required ulong Timestamp { get; init; }
}
