using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core;
using Server.Streaming;
using Server.Tunnel;
using Shared.Models.Events;
using Tests.Unit.Mocks;

namespace Tests.Unit.Tunnel;

[TestFixture]
public class TunnelServiceHandshakeTests
{
  /// <summary>
  /// SCENARIO:
  /// A peer opens a TCP connection to the tunnel port and then never starts the TLS handshake,
  /// as a port scanner does
  ///
  /// ACTION:
  /// Connect and send nothing
  ///
  /// EXPECTED RESULT:
  /// The server closes the connection once the handshake timeout passes instead of holding
  /// the socket open indefinitely
  /// </summary>
  [Test]
  public async Task SilentPeer_IsDisconnectedAfterHandshakeTimeout()
  {
    using var certs = new SelfSignedCerts();
    var endpoints = new ServerEndpoints();
    var service = new TunnelService(
      certs, endpoints, new FakePluginHost(), new SilentEventBus(), new ConnectionTracker(),
      null!, new StreamTapRegistry(), null!, NullLoggerFactory.Instance)
    {
      HandshakeTimeout = TimeSpan.FromMilliseconds(200)
    };
    await service.StartAsync(CancellationToken.None);

    try
    {
      using var peer = new TcpClient();
      await peer.ConnectAsync("127.0.0.1", endpoints.TunnelPort);

      Assert.That(await ServerClosesAsync(peer, TimeSpan.FromSeconds(5)), Is.True);
    }
    finally
    {
      await service.StopAsync();
    }
  }

  private static async Task<bool> ServerClosesAsync(TcpClient peer, TimeSpan within)
  {
    try
    {
      return await peer.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(within) == 0;
    }
    catch (IOException)
    {
      return true;
    }
  }

  private sealed class SelfSignedCerts : ICertificateService, IDisposable
  {
    private readonly RSA _key = RSA.Create(2048);

    public SelfSignedCerts()
    {
      var request = new CertificateRequest(
        "CN=tunnel-test", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      var now = DateTimeOffset.UtcNow;
      ServerCert = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
    }

    public bool HasCerts => true;
    public X509Certificate2 RootCa => ServerCert;
    public X509Certificate2 ServerCert { get; }
    public string RootCaPem => ServerCert.ExportCertificatePem();
    public ClientCertBundle GenerateClientCert(Guid clientId) => throw new NotSupportedException();
    public void GenerateCerts() => throw new NotSupportedException();
    public bool TryLoadCerts() => true;

    public void Dispose()
    {
      ServerCert.Dispose();
      _key.Dispose();
    }
  }

  private sealed class SilentEventBus : IEventBus
  {
    public Task PublishAsync<T>(T evt, CancellationToken ct) where T : ISystemEvent =>
      Task.CompletedTask;

    public async IAsyncEnumerable<T> SubscribeAsync<T>(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
      where T : ISystemEvent
    {
      await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
      yield break;
    }
  }
}
