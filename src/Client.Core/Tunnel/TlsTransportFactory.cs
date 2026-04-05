using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Client.Core.Platform;

namespace Client.Core.Tunnel;

public sealed class TlsTransportFactory : ITransportFactory
{
  public async Task<TransportConnection> ConnectAsync(
    string address, CredentialData creds, CancellationToken ct)
  {
    var (host, port) = ParseAddress(address);

    var caCert = X509Certificate2.CreateFromPem(creds.CaCert);
    var clientCert = X509Certificate2.CreateFromPem(creds.ClientCert, creds.ClientKey);

    var tcpClient = new TcpClient();
    try
    {
      using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      connectCts.CancelAfter(TimeSpan.FromSeconds(5));

      await tcpClient.ConnectAsync(host, port, connectCts.Token);

      var sslStream = new SslStream(tcpClient.GetStream(), false, (_, cert, _, _) =>
      {
        if (cert == null) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCert);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        using var serverCert = new X509Certificate2(cert);
        return chain.Build(serverCert);
      });

      try
      {
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
          TargetHost = host,
          ClientCertificates = [clientCert],
          EnabledSslProtocols = SslProtocols.Tls13,
          CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, ct);
      }
      catch
      {
        await sslStream.DisposeAsync();
        throw;
      }

      return new TransportConnection(sslStream, tcpClient, clientCert, caCert);
    }
    catch
    {
      tcpClient.Dispose();
      clientCert.Dispose();
      caCert.Dispose();
      throw;
    }
  }

  internal static (string Host, int Port) ParseAddress(string address)
  {
    if (address.StartsWith('['))
    {
      var closeBracket = address.IndexOf(']');
      if (closeBracket < 0) return (address, 4433);
      var ipv6Host = address[1..closeBracket];
      var rest = address[(closeBracket + 1)..];
      var ipv6Port = rest.StartsWith(':') && int.TryParse(rest[1..], out var p6) ? p6 : 4433;
      return (ipv6Host, ipv6Port);
    }
    var parts = address.Split(':', 2);
    var host = parts[0];
    var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 4433;
    return (host, port);
  }
}
