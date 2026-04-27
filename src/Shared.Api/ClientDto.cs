namespace Shared.Api;

public sealed class ClientDto
{
  public required Guid Id { get; init; }
  public required string Name { get; init; }
  public required ulong EnrolledAt { get; init; }
  public ulong? LastSeenAt { get; init; }
  public required bool Connected { get; init; }
}
