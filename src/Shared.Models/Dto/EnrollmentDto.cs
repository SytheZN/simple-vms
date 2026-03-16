namespace Shared.Models.Dto;

public sealed class EnrollRequest
{
  public required string Token { get; init; }
}

public sealed class EnrollResponse
{
  public required string[] Addresses { get; init; }
  public required string Ca { get; init; }
  public required string Cert { get; init; }
  public required string Key { get; init; }
  public required Guid ClientId { get; init; }
}

public sealed class StartEnrollmentResponse
{
  public required string Token { get; init; }
  public required string QrData { get; init; }
}

public sealed class QrPayload
{
  public required int V { get; init; }
  public required string[] Addresses { get; init; }
  public required string Token { get; init; }
}
