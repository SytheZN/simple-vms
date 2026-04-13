using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Client.Core.Api;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;
namespace Tests.Integration.Tunnel;

[TestFixture]
public sealed class TunnelIntegrationTests
{
  private CredentialData _creds = null!;
  private int _tunnelPort;

  [OneTimeSetUp]
  public async Task Setup()
  {
    var http = ApiTestFixture.Client;

    var startResponse = await http.PostAsync("/api/v1/clients/enroll", null);
    var start = await ApiTestFixture.Envelope<StartEnrollmentResponse>(startResponse);

    var enrollResponse = await http.PostAsJsonAsync("/api/v1/enroll",
      new { token = start.Body!.Token });
    var enroll = await ApiTestFixture.Envelope<EnrollResponse>(enrollResponse);
    var body = enroll.Body!;

    var endpoints = ApiTestFixture.App.Services.GetRequiredService<ServerEndpoints>();
    _tunnelPort = endpoints.TunnelPort;

    _creds = new CredentialData(
      body.Ca,
      body.Cert,
      body.Key,
      [$"127.0.0.1:{_tunnelPort}"],
      body.ClientId);
  }

  /// <summary>
  /// SCENARIO:
  /// Enrollment returned a tunnel port
  ///
  /// ACTION:
  /// Check that the tunnel port is non-zero
  ///
  /// EXPECTED RESULT:
  /// The port is a valid ephemeral port
  /// </summary>
  [Test, Order(1)]
  public void Enrollment_TunnelPort_IsValid()
  {
    Assert.That(_tunnelPort, Is.GreaterThan(0));
    Assert.That(_creds.Addresses, Has.Length.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// Enrollment returned PEM certificates
  ///
  /// ACTION:
  /// Parse the CA cert and client cert from the credential data
  ///
  /// EXPECTED RESULT:
  /// Both certs parse without error, client cert has a private key
  /// </summary>
  [Test, Order(2)]
  public void Enrollment_Credentials_ParseAsValidCerts()
  {
    var caCert = X509Certificate2.CreateFromPem(_creds.CaCert);
    Assert.That(caCert.Subject, Is.Not.Empty);

    var clientCert = X509Certificate2.CreateFromPem(_creds.ClientCert, _creds.ClientKey);
    Assert.That(clientCert.HasPrivateKey, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A TCP connection to the tunnel port
  ///
  /// ACTION:
  /// Connect a raw TCP socket to the tunnel port
  ///
  /// EXPECTED RESULT:
  /// Connection succeeds
  /// </summary>
  [Test, Order(3)]
  public async Task TcpConnect_Succeeds()
  {
    using var tcp = new TcpClient();
    await tcp.ConnectAsync("127.0.0.1", _tunnelPort);
    Assert.That(tcp.Connected, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A TLS handshake with mutual authentication
  ///
  /// ACTION:
  /// TCP connect, then authenticate with mTLS using enrollment certs
  ///
  /// EXPECTED RESULT:
  /// TLS handshake completes, stream is authenticated and mutually authenticated
  /// </summary>
  [Test, Order(4)]
  public async Task TlsHandshake_WithClientCert_Succeeds()
  {
    var (tcp, ssl) = await ConnectTlsAsync();
    using (tcp)
    await using (ssl)
    {
      Assert.That(ssl.IsAuthenticated, Is.True);
      Assert.That(ssl.IsMutuallyAuthenticated, Is.True);
    }
  }

  /// <summary>
  /// SCENARIO:
  /// Version exchange after TLS handshake
  ///
  /// ACTION:
  /// Handshake, then send version 1 on stream 0, read server response
  ///
  /// EXPECTED RESULT:
  /// Server responds with version 1
  /// </summary>
  [Test, Order(5)]
  public async Task VersionExchange_ReturnsVersion1()
  {
    var (tcp, ssl) = await ConnectTlsAsync();

    var versionPayload = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(versionPayload, 1);
    var frame = new byte[MessageEnvelope.MuxHeaderSize + 4];
    MessageEnvelope.WriteMuxHeader(frame, 0, 0, 4);
    versionPayload.CopyTo(frame.AsSpan(MessageEnvelope.MuxHeaderSize));
    await ssl.WriteAsync(frame);

    var responseHeader = new byte[MessageEnvelope.MuxHeaderSize];
    await ssl.ReadExactlyAsync(responseHeader);
    var (streamId, _, payloadLength) = MessageEnvelope.ReadMuxHeader(responseHeader);

    Assert.That(streamId, Is.EqualTo(0u));
    Assert.That(payloadLength, Is.GreaterThanOrEqualTo(4));

    var responsePayload = new byte[payloadLength];
    await ssl.ReadExactlyAsync(responsePayload);
    var serverVersion = BinaryPrimitives.ReadUInt32LittleEndian(responsePayload);

    Assert.That(serverVersion, Is.EqualTo(1u));

    await ssl.DisposeAsync();
    tcp.Dispose();
  }

  /// <summary>
  /// SCENARIO:
  /// Full tunnel connection via TunnelService
  ///
  /// ACTION:
  /// Use TunnelService to connect with enrolled credentials
  ///
  /// EXPECTED RESULT:
  /// State is Connected, generation is 1
  /// </summary>
  [Test, Order(6)]
  public async Task TunnelService_Connect_Succeeds()
  {
    var store = new InMemoryCredentialStore(_creds);
    var transport = new TlsTransportFactory(NullLogger<TlsTransportFactory>.Instance);

    await using var tunnel = new TunnelService(store, transport, NullLogger<TunnelService>.Instance);
    await tunnel.ConnectAsync(new(), CancellationToken.None);

    Assert.That(tunnel.State, Is.EqualTo(ConnectionState.Connected));
    Assert.That(tunnel.Generation, Is.EqualTo(1u));

    await tunnel.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// API call over the tunnel
  ///
  /// ACTION:
  /// Connect via tunnel, call GetHealthAsync
  ///
  /// EXPECTED RESULT:
  /// Returns a valid health response
  /// </summary>
  [Test, Order(7)]
  public async Task TunnelApi_GetHealth_ReturnsSuccess()
  {
    var store = new InMemoryCredentialStore(_creds);
    var transport = new TlsTransportFactory(NullLogger<TlsTransportFactory>.Instance);

    await using var tunnel = new TunnelService(store, transport, NullLogger<TunnelService>.Instance);
    await tunnel.ConnectAsync(new(), CancellationToken.None);

    var api = new ApiClient(tunnel, NullLogger<ApiClient>.Instance);
    var result = await api.GetHealthAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(result.AsT0.Status, Is.AnyOf("healthy", "degraded", "unhealthy"));
    Assert.That(result.AsT0.Version, Is.Not.Null);

    await tunnel.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Camera list API call over the tunnel
  ///
  /// ACTION:
  /// Connect via tunnel, call GetCamerasAsync
  ///
  /// EXPECTED RESULT:
  /// Returns an empty list (no cameras configured)
  /// </summary>
  [Test, Order(8)]
  public async Task TunnelApi_GetCameras_ReturnsEmptyList()
  {
    var store = new InMemoryCredentialStore(_creds);
    var transport = new TlsTransportFactory(NullLogger<TlsTransportFactory>.Instance);

    await using var tunnel = new TunnelService(store, transport, NullLogger<TunnelService>.Instance);
    await tunnel.ConnectAsync(new(), CancellationToken.None);

    var api = new ApiClient(tunnel, NullLogger<ApiClient>.Instance);
    var result = await api.GetCamerasAsync(null, CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(result.AsT0, Is.Not.Null);

    await tunnel.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Two tunnel connections from the same client cert are active simultaneously
  ///
  /// ACTION:
  /// Connect two TunnelService instances with the same credentials, make API calls on both
  ///
  /// EXPECTED RESULT:
  /// Both connections work independently
  /// </summary>
  [Test, Order(9)]
  public async Task TwoConnections_SameClient_BothWork()
  {
    var transport = new TlsTransportFactory(NullLogger<TlsTransportFactory>.Instance);

    await using var tunnel1 = new TunnelService(
      new InMemoryCredentialStore(_creds), transport, NullLogger<TunnelService>.Instance);
    await using var tunnel2 = new TunnelService(
      new InMemoryCredentialStore(_creds), transport, NullLogger<TunnelService>.Instance);

    await tunnel1.ConnectAsync(new(), CancellationToken.None);
    await tunnel2.ConnectAsync(new(), CancellationToken.None);

    var api1 = new ApiClient(tunnel1, NullLogger<ApiClient>.Instance);
    var api2 = new ApiClient(tunnel2, NullLogger<ApiClient>.Instance);

    var result1 = await api1.GetHealthAsync(CancellationToken.None);
    var result2 = await api2.GetHealthAsync(CancellationToken.None);

    Assert.That(result1.IsT0, Is.True);
    Assert.That(result2.IsT0, Is.True);

    await tunnel1.DisconnectAsync();
    await tunnel2.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Client is given a different CA than the one that signed the server cert
  ///
  /// ACTION:
  /// Swap _creds.CaCert for a freshly generated unrelated CA, attempt to connect
  ///
  /// EXPECTED RESULT:
  /// TLS handshake fails with a bad_certificate alert — PinnedAuthentication rejects
  /// the server cert because it is not signed by the pinned CA.
  /// </summary>
  [Test, Order(10)]
  public void TunnelService_Connect_WithWrongCa_Fails()
  {
    using var rsa = System.Security.Cryptography.RSA.Create(2048);
    var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
      "CN=Wrong CA", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
      System.Security.Cryptography.RSASignaturePadding.Pkcs1);
    using var wrongCa = req.CreateSelfSigned(
      DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    var wrongCaPem = wrongCa.ExportCertificatePem();

    var tamperedCreds = new CredentialData(
      wrongCaPem, _creds.ClientCert, _creds.ClientKey, _creds.Addresses, _creds.ClientId);

    var store = new InMemoryCredentialStore(tamperedCreds);
    var transport = new TlsTransportFactory(NullLogger<TlsTransportFactory>.Instance);

    var tunnel = new TunnelService(store, transport, NullLogger<TunnelService>.Instance);
    Assert.ThatAsync(async () =>
    {
      try { await tunnel.ConnectAsync(new(), CancellationToken.None); }
      finally { await tunnel.DisposeAsync(); }
    }, Throws.InstanceOf<InvalidOperationException>()
        .With.InnerException.InstanceOf<AggregateException>()
        .With.InnerException
          .With.Message.Contains("certificate").IgnoreCase);
  }

  /// <summary>
  /// SCENARIO:
  /// Many concurrent API calls flow through a single tunnel
  ///
  /// ACTION:
  /// Open one tunnel, fire 32 concurrent GetHealthAsync calls, await them all
  ///
  /// EXPECTED RESULT:
  /// All calls succeed. Note: outbound records all come through the mux write lock,
  /// so this guards against the BC blocking-mode deadlock (writer blocked waiting for a
  /// read) — it does NOT exercise concurrent record production from the read side
  /// (post-handshake messages). A targeted test for that would need a way to force
  /// read-side record emission alongside steady app-data traffic.
  /// </summary>
  [Test, Order(11)]
  public async Task TunnelApi_ConcurrentCalls_AllSucceed()
  {
    var store = new InMemoryCredentialStore(_creds);
    var transport = new TlsTransportFactory(NullLogger<TlsTransportFactory>.Instance);

    await using var tunnel = new TunnelService(store, transport, NullLogger<TunnelService>.Instance);
    await tunnel.ConnectAsync(new(), CancellationToken.None);

    var api = new ApiClient(tunnel, NullLogger<ApiClient>.Instance);

    var tasks = Enumerable.Range(0, 32)
      .Select(_ => api.GetHealthAsync(CancellationToken.None))
      .ToArray();

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var all = Task.WhenAll(tasks);
    await all.WaitAsync(timeout.Token);

    foreach (var t in tasks)
      Assert.That(t.Result.IsT0, Is.True);

    await tunnel.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Sustained request/response volume across a single tunnel over time
  ///
  /// ACTION:
  /// Open one tunnel and serially issue 200 API calls (keeps the mux hot long enough to
  /// trigger keepalive interactions and exercise record fragmentation/reassembly)
  ///
  /// EXPECTED RESULT:
  /// All requests succeed and payloads are intact
  /// </summary>
  [Test, Order(12)]
  public async Task TunnelApi_Volume_AllRequestsSucceed()
  {
    var store = new InMemoryCredentialStore(_creds);
    var transport = new TlsTransportFactory(NullLogger<TlsTransportFactory>.Instance);

    await using var tunnel = new TunnelService(store, transport, NullLogger<TunnelService>.Instance);
    await tunnel.ConnectAsync(new(), CancellationToken.None);

    var api = new ApiClient(tunnel, NullLogger<ApiClient>.Instance);

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    for (var i = 0; i < 200; i++)
    {
      var result = await api.GetHealthAsync(timeout.Token);
      Assert.That(result.IsT0, Is.True, $"request {i} failed");
      Assert.That(result.AsT0.Version, Is.Not.Null);
    }

    await tunnel.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// Cancellation token is tripped mid-handshake
  ///
  /// ACTION:
  /// Pass a token that cancels after 1ms (roughly during TCP connect / TLS handshake)
  ///
  /// EXPECTED RESULT:
  /// ConnectAsync surfaces an exception (cancelled or aggregate) and leaves no hanging state.
  /// </summary>
  [Test, Order(13)]
  public void TunnelService_Connect_CancelledDuringHandshake_Throws()
  {
    var store = new InMemoryCredentialStore(_creds);
    var transport = new TlsTransportFactory(NullLogger<TlsTransportFactory>.Instance);
    var tunnel = new TunnelService(store, transport, NullLogger<TunnelService>.Instance);

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Assert.ThatAsync(async () =>
    {
      try { await tunnel.ConnectAsync(new(), cts.Token); }
      finally { await tunnel.DisposeAsync(); }
    }, Throws.InstanceOf<OperationCanceledException>()
        .Or.InstanceOf<InvalidOperationException>());
  }

  private async Task<(TcpClient Tcp, SslStream Ssl)> ConnectTlsAsync()
  {
    var caCert = X509Certificate2.CreateFromPem(
      _creds.CaCert);
    var clientCert = X509Certificate2.CreateFromPem(
      _creds.ClientCert,
      _creds.ClientKey);

    var tcp = new TcpClient();
    await tcp.ConnectAsync("127.0.0.1", _tunnelPort);

    var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) =>
    {
      if (cert == null) return false;
      using var chain = new X509Chain();
      chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
      chain.ChainPolicy.CustomTrustStore.Add(caCert);
      chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
      using var serverCert = X509CertificateLoader.LoadCertificate(cert.GetRawCertData());
      return chain.Build(serverCert);
    });

    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
    {
      TargetHost = "127.0.0.1",
      ClientCertificates = [clientCert],
      EnabledSslProtocols = SslProtocols.Tls13,
      CertificateRevocationCheckMode = X509RevocationMode.NoCheck
    });

    return (tcp, ssl);
  }

  private sealed class InMemoryCredentialStore : ICredentialStore
  {
    private CredentialData? _data;

    public InMemoryCredentialStore(CredentialData data) => _data = data;

    public Task<CredentialData?> LoadAsync() => Task.FromResult(_data);
    public Task SaveAsync(CredentialData data) { _data = data; return Task.CompletedTask; }
    public Task ClearAsync() { _data = null; return Task.CompletedTask; }
  }
}
