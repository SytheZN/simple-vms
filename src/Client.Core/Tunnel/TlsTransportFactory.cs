using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Client.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Client.Core.Tunnel;

public sealed class TlsTransportFactory : ITransportFactory
{
  private readonly ILogger<TlsTransportFactory> _logger;

  public TlsTransportFactory(ILogger<TlsTransportFactory> logger)
  {
    _logger = logger;
  }

  public async Task<TransportConnection> ConnectAsync(
    string address, CredentialData creds, CancellationToken ct)
  {
    var (host, port) = ParseAddress(address);
    _logger.LogDebug("Connecting to {Host}:{Port}", host, port);

    _logger.LogDebug("Loading certificates from PEM");
    var caCert = X509Certificate2.CreateFromPem(creds.CaCert);
    var clientCert = X509Certificate2.CreateFromPem(creds.ClientCert, creds.ClientKey);
    _logger.LogDebug("Certificates loaded, CA subject={CaSubject}, client subject={ClientSubject}",
      caCert.Subject, clientCert.Subject);

    var tcpClient = new TcpClient();
    try
    {
      using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      connectCts.CancelAfter(TimeSpan.FromSeconds(5));

      try
      {
        await tcpClient.ConnectAsync(host, port, connectCts.Token);
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        throw new TimeoutException($"Connection to {host}:{port} timed out");
      }
      _logger.LogDebug("TCP connected to {Host}:{Port}", host, port);

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
        _logger.LogDebug("Starting TLS handshake");
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
          TargetHost = host,
          ClientCertificates = [clientCert],
          EnabledSslProtocols = SslProtocols.Tls13,
          CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, ct);
        _logger.LogDebug("TLS handshake completed, protocol={Protocol}", sslStream.SslProtocol);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "TLS handshake failed");
        await sslStream.DisposeAsync();
        throw;
      }

      return new TransportConnection(sslStream, tcpClient, clientCert, caCert);
    }
    catch (Exception ex) when (ex is not AuthenticationException)
    {
      _logger.LogError(ex, "Connection to {Host}:{Port} failed", host, port);
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
