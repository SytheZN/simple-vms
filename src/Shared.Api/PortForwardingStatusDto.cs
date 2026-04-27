namespace Shared.Api;

public sealed class PortForwardingStatusDto
{
  public required bool Active { get; init; }
  public string? Protocol { get; init; }
  public int? ExternalPort { get; init; }
  public int? InternalPort { get; init; }
  public string? LastError { get; init; }
  public ulong? LastAppliedAtMicros { get; init; }
}
