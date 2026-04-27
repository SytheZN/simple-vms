using System.Security.Cryptography.X509Certificates;

namespace Shared.Models;

public interface ICertificateService
{
  bool HasCerts { get; }
  X509Certificate2 RootCa { get; }
  X509Certificate2 ServerCert { get; }
  string RootCaPem { get; }
  ClientCertBundle GenerateClientCert(Guid clientId);
  void GenerateCerts();
  bool TryLoadCerts();
}
