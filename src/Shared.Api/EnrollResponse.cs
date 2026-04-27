namespace Shared.Api;

public sealed class EnrollResponse
{
  public required string[] Addresses { get; init; }
  public required string Ca { get; init; }
  public required string Cert { get; init; }
  public required string Key { get; init; }
  public required Guid ClientId { get; init; }
}
