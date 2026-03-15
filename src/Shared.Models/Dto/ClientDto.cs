namespace Shared.Models.Dto;

public sealed class ClientListItem
{
  public required Guid Id { get; init; }
  public required string Name { get; init; }
  public required ulong EnrolledAt { get; init; }
  public ulong? LastSeenAt { get; init; }
  public required bool Connected { get; init; }
}

public sealed class UpdateClientRequest
{
  public required string Name { get; init; }
}
