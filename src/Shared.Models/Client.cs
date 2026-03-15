namespace Shared.Models;

public sealed class Client
{
  public required Guid Id { get; set; }
  public required string Name { get; set; }
  public required string CertificateSerial { get; set; }
  public bool Revoked { get; set; }
  public required ulong EnrolledAt { get; set; }
  public ulong? LastSeenAt { get; set; }
}
