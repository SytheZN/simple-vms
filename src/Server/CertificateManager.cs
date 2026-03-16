using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Shared.Models;

namespace Server;

public sealed class CertificateManager : ICertificateService
{
  private readonly string _certsPath;

  private X509Certificate2? _rootCa;
  private X509Certificate2? _serverCert;

  public X509Certificate2 RootCa => _rootCa
    ?? throw new InvalidOperationException("Root CA not initialized");
  public X509Certificate2 ServerCert => _serverCert
    ?? throw new InvalidOperationException("Server certificate not initialized");

  public bool IsFirstRun => !File.Exists(RootCaKeyPath);
  public string RootCaPem => RootCa.ExportCertificatePem();

  private string RootCaPath => Path.Combine(_certsPath, "root-ca.pem");
  private string RootCaKeyPath => Path.Combine(_certsPath, "root-ca.key");
  private string ServerCertPath => Path.Combine(_certsPath, "server.pem");
  private string ServerCertKeyPath => Path.Combine(_certsPath, "server.key");

  public CertificateManager(IConfiguration config)
  {
    var dataPath = config["data-path"]!;
    _certsPath = Path.Combine(dataPath, "certs");
  }

  public void Initialize()
  {
    Directory.CreateDirectory(_certsPath);

    if (IsFirstRun)
    {
      _rootCa = GenerateRootCa();
      SaveCertAndKey(_rootCa, RootCaPath, RootCaKeyPath);

      _serverCert = GenerateServerCert(_rootCa);
      SaveCertAndKey(_serverCert, ServerCertPath, ServerCertKeyPath);
    }
    else
    {
      _rootCa = LoadCertWithKey(RootCaPath, RootCaKeyPath);
      _serverCert = LoadCertWithKey(ServerCertPath, ServerCertKeyPath);
    }
  }

  public ClientCertBundle GenerateClientCert(Guid clientId)
  {
    var serial = GenerateSerial();
    using var key = RSA.Create(2048);

    var subject = new X500DistinguishedName($"CN={clientId}");
    var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    request.CertificateExtensions.Add(
      new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

    request.CertificateExtensions.Add(
      new X509EnhancedKeyUsageExtension(
        [new Oid("1.3.6.1.5.5.7.3.2")],
        critical: false));

    request.CertificateExtensions.Add(
      new X509BasicConstraintsExtension(false, false, 0, true));

    var serialBytes = HexToBytes(serial);

    var cert = request.Create(
      RootCa,
      DateTimeOffset.UtcNow,
      DateTimeOffset.UtcNow.AddYears(10),
      serialBytes);

    using var withKey = cert.CopyWithPrivateKey(key);

    return new ClientCertBundle
    {
      CertPem = withKey.ExportCertificatePem(),
      KeyPem = key.ExportRSAPrivateKeyPem(),
      Serial = serial
    };
  }

  private string GenerateSerial()
  {
    var bytes = new byte[16];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToHexStringLower(bytes);
  }

  private static X509Certificate2 GenerateRootCa()
  {
    using var key = RSA.Create(4096);

    var subject = new X500DistinguishedName("CN=VMS Root CA");
    var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    request.CertificateExtensions.Add(
      new X509BasicConstraintsExtension(true, true, 1, true));

    request.CertificateExtensions.Add(
      new X509KeyUsageExtension(
        X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
        critical: true));

    request.CertificateExtensions.Add(
      new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

    var serial = new byte[16];
    RandomNumberGenerator.Fill(serial);

    return request.CreateSelfSigned(
      DateTimeOffset.UtcNow,
      DateTimeOffset.UtcNow.AddYears(25));
  }

  private static X509Certificate2 GenerateServerCert(X509Certificate2 rootCa)
  {
    using var key = RSA.Create(2048);

    var subject = new X500DistinguishedName("CN=VMS Server");
    var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    request.CertificateExtensions.Add(
      new X509KeyUsageExtension(
        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
        critical: true));

    request.CertificateExtensions.Add(
      new X509EnhancedKeyUsageExtension(
        [new Oid("1.3.6.1.5.5.7.3.1")],
        critical: false));

    request.CertificateExtensions.Add(
      new X509BasicConstraintsExtension(false, false, 0, true));

    var serial = new byte[16];
    RandomNumberGenerator.Fill(serial);

    var cert = request.Create(
      rootCa,
      DateTimeOffset.UtcNow,
      DateTimeOffset.UtcNow.AddYears(10),
      serial);

    return cert.CopyWithPrivateKey(key);
  }

  private static void SaveCertAndKey(X509Certificate2 cert, string certPath, string keyPath)
  {
    File.WriteAllText(certPath, cert.ExportCertificatePem());
    File.WriteAllText(keyPath, cert.GetRSAPrivateKey()!.ExportRSAPrivateKeyPem());
  }

  private static X509Certificate2 LoadCertWithKey(string certPath, string keyPath)
  {
    var certPem = File.ReadAllText(certPath);
    var keyPem = File.ReadAllText(keyPath);
    return X509Certificate2.CreateFromPem(certPem, keyPem);
  }

  private static byte[] HexToBytes(string hex)
  {
    var bytes = new byte[hex.Length / 2];
    for (var i = 0; i < bytes.Length; i++)
      bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
    return bytes;
  }
}
