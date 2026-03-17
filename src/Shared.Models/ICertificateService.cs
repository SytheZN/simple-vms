namespace Shared.Models;

public sealed class ClientCertBundle
{
  public required string CertPem { get; init; }
  public required string KeyPem { get; init; }
  public required string Serial { get; init; }
}

public interface ICertificateService
{
  bool HasCerts { get; }
  string RootCaPem { get; }
  ClientCertBundle GenerateClientCert(Guid clientId);
  void GenerateCerts();
  bool TryLoadCerts();
}
