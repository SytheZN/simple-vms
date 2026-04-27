namespace Shared.Api;

public sealed class HealthResponse
{
  public required string Status { get; init; }
  public required int Uptime { get; init; }
  public required string Version { get; init; }
  public required int TunnelPort { get; init; }
  public required int HttpPort { get; init; }
  public string[]? MissingSettings { get; init; }
}
