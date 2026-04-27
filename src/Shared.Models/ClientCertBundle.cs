namespace Shared.Models;

public sealed class ClientCertBundle
{
  public required string CertPem { get; init; }
  public required string KeyPem { get; init; }
  public required string Serial { get; init; }
}
