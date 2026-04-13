using System.Buffers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Client.Core.Platform;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;

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

    var caCert = ReadPemCertificate(creds.CaCert);
    var clientCert = ReadPemCertificate(creds.ClientCert);
    var clientKey = ReadPemPrivateKey(creds.ClientKey);
    _logger.LogDebug("Certificates loaded, CA subject={CaSubject}, client subject={ClientSubject}",
      caCert.SubjectDN, clientCert.SubjectDN);

    var tcpClient = new TcpClient { NoDelay = true };
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

      var crypto = new BcTlsCrypto();
      var protocol = new TlsClientProtocol();
      var tlsClient = new PinnedTlsClient(crypto, caCert, clientCert, clientKey);
      var netStream = tcpClient.GetStream();

      try
      {
        _logger.LogDebug("Starting TLS handshake");
        protocol.Connect(tlsClient);
        await DriveHandshakeAsync(protocol, tlsClient, netStream, ct);
        _logger.LogDebug("TLS handshake completed");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "TLS handshake failed");
        try { protocol.Close(); }
        catch (Exception closeEx)
        {
          _logger.LogDebug(closeEx, "protocol.Close() during handshake failure");
        }
        throw;
      }

      return new TransportConnection(new NonBlockingTlsStream(protocol, netStream), tcpClient);
    }
    catch
    {
      tcpClient.Dispose();
      throw;
    }
  }

  private static BcX509Certificate ReadPemCertificate(string pem)
  {
    using var reader = new StringReader(pem);
    var pemReader = new PemReader(reader);
    if (pemReader.ReadObject() is BcX509Certificate cert) return cert;
    throw new InvalidOperationException("No certificate found in PEM");
  }

  private static AsymmetricKeyParameter ReadPemPrivateKey(string pem)
  {
    using var reader = new StringReader(pem);
    var pemReader = new PemReader(reader);
    var obj = pemReader.ReadObject();
    return obj switch
    {
      AsymmetricCipherKeyPair kp => kp.Private,
      AsymmetricKeyParameter k when k.IsPrivate => k,
      _ => throw new InvalidOperationException(
        $"Unexpected PEM object for private key: {obj?.GetType().Name ?? "null"}")
    };
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

  private static async Task DriveHandshakeAsync(
    TlsClientProtocol protocol, PinnedTlsClient tlsClient, Stream net, CancellationToken ct)
  {
    var ioBuf = ArrayPool<byte>.Shared.Rent(16384);
    try
    {
      while (!tlsClient.HandshakeComplete)
      {
        await FlushOutputAsync(protocol, net, ct);
        if (tlsClient.HandshakeComplete) break;

        var n = await net.ReadAsync(ioBuf.AsMemory(0, 16384), ct);
        if (n <= 0)
          throw new IOException("Connection closed during TLS handshake");
        protocol.OfferInput(ioBuf, 0, n);
      }
      await FlushOutputAsync(protocol, net, ct);
    }
    finally { ArrayPool<byte>.Shared.Return(ioBuf); }
  }

  private static async Task FlushOutputAsync(
    TlsClientProtocol protocol, Stream net, CancellationToken ct)
  {
    var avail = protocol.GetAvailableOutputBytes();
    if (avail <= 0) return;
    var buf = ArrayPool<byte>.Shared.Rent(avail);
    try
    {
      var read = protocol.ReadOutput(buf, 0, avail);
      if (read > 0)
        await net.WriteAsync(buf.AsMemory(0, read), ct);
    }
    finally { ArrayPool<byte>.Shared.Return(buf); }
  }

  private sealed class NonBlockingTlsStream : Stream
  {
    private readonly TlsClientProtocol _protocol;
    private readonly Stream _transport;
    private readonly SemaphoreSlim _protocolLock = new(1, 1);
    private readonly SemaphoreSlim _socketReadLock = new(1, 1);
    private bool _disposed;

    public NonBlockingTlsStream(TlsClientProtocol protocol, Stream transport)
    {
      _protocol = protocol;
      _transport = transport;
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException("Use ReadAsync");

    public override void Write(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException("Use WriteAsync");

    public override async Task<int> ReadAsync(
      byte[] buffer, int offset, int count, CancellationToken ct)
    {
      byte[]? rbuf = null;
      try
      {
        while (true)
        {
          ct.ThrowIfCancellationRequested();

          int consumed = 0;
          await _protocolLock.WaitAsync(ct);
          try
          {
            var available = _protocol.GetAvailableInputBytes();
            if (available > 0)
            {
              var toRead = Math.Min(available, count);
              consumed = _protocol.ReadInput(buffer, offset, toRead);
            }
          }
          finally { _protocolLock.Release(); }

          if (consumed > 0)
            return consumed;

          rbuf ??= ArrayPool<byte>.Shared.Rent(16384);

          int n;
          await _socketReadLock.WaitAsync(ct);
          try { n = await _transport.ReadAsync(rbuf.AsMemory(0, 16384), ct); }
          finally { _socketReadLock.Release(); }

          if (n <= 0)
          {
            // Transport EOF: one final drain for any plaintext already decrypted
            // into the input buffer (e.g. last app-data record co-arriving with close-notify).
            await _protocolLock.WaitAsync(ct);
            try
            {
              var tail = _protocol.GetAvailableInputBytes();
              if (tail > 0)
                return _protocol.ReadInput(buffer, offset, Math.Min(tail, count));
            }
            finally { _protocolLock.Release(); }
            return 0;
          }

          // TLS 1.3 AEAD keys its nonce on the implicit sequence number — records
          // must reach the wire in encryption order, so lock spans encrypt + send.
          await _protocolLock.WaitAsync(ct);
          try
          {
            _protocol.OfferInput(rbuf, 0, n);
            var outAvail = _protocol.GetAvailableOutputBytes();
            if (outAvail > 0)
            {
              var pending = ArrayPool<byte>.Shared.Rent(outAvail);
              try
              {
                var pendingLen = _protocol.ReadOutput(pending, 0, outAvail);
                await _transport.WriteAsync(pending.AsMemory(0, pendingLen), ct);
              }
              finally { ArrayPool<byte>.Shared.Return(pending); }
            }
          }
          finally { _protocolLock.Release(); }
        }
      }
      finally
      {
        if (rbuf != null)
          ArrayPool<byte>.Shared.Return(rbuf);
      }
    }

    public override async Task WriteAsync(
      byte[] buffer, int offset, int count, CancellationToken ct)
    {
      // Hold the protocol lock across encrypt + socket write: see record-ordering note in ReadAsync.
      await _protocolLock.WaitAsync(ct);
      try
      {
        _protocol.WriteApplicationData(buffer, offset, count);
        var avail = _protocol.GetAvailableOutputBytes();
        if (avail <= 0) return;

        var outBytes = ArrayPool<byte>.Shared.Rent(avail);
        try
        {
          var outLen = _protocol.ReadOutput(outBytes, 0, avail);
          await _transport.WriteAsync(outBytes.AsMemory(0, outLen), ct);
        }
        finally { ArrayPool<byte>.Shared.Return(outBytes); }
      }
      finally { _protocolLock.Release(); }
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
      if (MemoryMarshal.TryGetArray<byte>(buffer, out var seg))
        return new ValueTask<int>(ReadAsync(seg.Array!, seg.Offset, seg.Count, ct));
      return ReadAsyncFallback(buffer, ct);
    }

    private async ValueTask<int> ReadAsyncFallback(Memory<byte> dest, CancellationToken ct)
    {
      var rented = ArrayPool<byte>.Shared.Rent(dest.Length);
      try
      {
        var n = await ReadAsync(rented, 0, dest.Length, ct);
        rented.AsMemory(0, n).CopyTo(dest);
        return n;
      }
      finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    public override ValueTask WriteAsync(
      ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
      if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> seg))
        return new ValueTask(WriteAsync(seg.Array!, seg.Offset, seg.Count, ct));

      var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
      buffer.CopyTo(rented);
      return new ValueTask(WriteAsyncWithRent(rented, buffer.Length, ct));
    }

    private async Task WriteAsyncWithRent(byte[] rented, int len, CancellationToken ct)
    {
      try { await WriteAsync(rented, 0, len, ct); }
      finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    protected override void Dispose(bool disposing)
    {
      if (_disposed) return;
      _disposed = true;
      if (disposing)
      {
        try { _protocol.Close(); }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"TlsClientProtocol.Close failed: {ex}");
        }
      }
      base.Dispose(disposing);
    }
  }

  // Exactly one signature scheme: rsa_pss_rsae_sha256 (0x0804). Server is under our control
  // and always issues RSA-2048 leaf certs, so we don't negotiate.
  private static readonly SignatureAndHashAlgorithm PinnedSignatureAlgorithm =
    new(HashAlgorithm.Intrinsic, SignatureAlgorithm.rsa_pss_rsae_sha256);

  // Exactly one TLS 1.3 cipher suite — narrows attack surface to a single modern AEAD.
  private static readonly int[] PinnedCipherSuites = [CipherSuite.TLS_AES_128_GCM_SHA256];

  private sealed class PinnedTlsClient : DefaultTlsClient
  {
    private readonly BcX509Certificate _caCert;
    private readonly BcX509Certificate _clientCert;
    private readonly AsymmetricKeyParameter _clientKey;

    public PinnedTlsClient(
      BcTlsCrypto crypto,
      BcX509Certificate caCert,
      BcX509Certificate clientCert,
      AsymmetricKeyParameter clientKey) : base(crypto)
    {
      _caCert = caCert;
      _clientCert = clientCert;
      _clientKey = clientKey;
    }

    public bool HandshakeComplete { get; private set; }

    public override void NotifyHandshakeComplete()
    {
      base.NotifyHandshakeComplete();
      HandshakeComplete = true;
    }

    protected override ProtocolVersion[] GetSupportedVersions() =>
      ProtocolVersion.TLSv13.Only();

    protected override int[] GetSupportedCipherSuites() => PinnedCipherSuites;

    protected override IList<SignatureAndHashAlgorithm> GetSupportedSignatureAlgorithms() =>
      [PinnedSignatureAlgorithm];

    // Server cert is self-issued by the pinned CA and has no SNI: suppress the extension entirely.
    protected override IList<ServerName>? GetSniServerNames() => null;

    public override TlsAuthentication GetAuthentication() =>
      new PinnedAuthentication(m_context, (BcTlsCrypto)Crypto, _caCert, _clientCert, _clientKey);
  }

  private sealed class PinnedAuthentication : TlsAuthentication
  {
    private readonly TlsContext _context;
    private readonly BcTlsCrypto _crypto;
    private readonly BcX509Certificate _caCert;
    private readonly BcX509Certificate _clientCert;
    private readonly AsymmetricKeyParameter _clientKey;

    public PinnedAuthentication(
      TlsContext context,
      BcTlsCrypto crypto,
      BcX509Certificate caCert,
      BcX509Certificate clientCert,
      AsymmetricKeyParameter clientKey)
    {
      _context = context;
      _crypto = crypto;
      _caCert = caCert;
      _clientCert = clientCert;
      _clientKey = clientKey;
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
      var chain = serverCertificate?.Certificate;
      if (chain == null || chain.IsEmpty)
        throw new TlsFatalAlert(AlertDescription.bad_certificate);

      // Single-tier CA: leaf is signed directly by the pinned root. The CA also issues
      // client certs (id-kp-clientAuth); the serverAuth EKU check here is what prevents
      // a leaked client cert from being usable as a server cert.
      try
      {
        var leafEncoded = chain.GetCertificateAt(0).GetEncoded();
        var leaf = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(leafEncoded);
        leaf.Verify(_caCert.GetPublicKey());
        leaf.CheckValidity();

        var ekus = leaf.GetExtendedKeyUsage();
        if (ekus == null || !ekus.Contains(KeyPurposeID.id_kp_serverAuth))
          throw new InvalidOperationException("Server cert missing serverAuth EKU");
      }
      catch (Exception ex)
      {
        throw new TlsFatalAlert(AlertDescription.bad_certificate, ex);
      }
    }

    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
      // Surface server/client disagreement immediately rather than at verify-time.
      var serverAlgs = certificateRequest?.SupportedSignatureAlgorithms;
      if (serverAlgs != null && !serverAlgs.Any(a =>
        a.Hash == PinnedSignatureAlgorithm.Hash &&
        a.Signature == PinnedSignatureAlgorithm.Signature))
      {
        throw new TlsFatalAlert(AlertDescription.handshake_failure,
          new InvalidOperationException("Server does not accept pinned signature scheme"));
      }

      var tlsCert = (TlsCertificate)new BcTlsCertificate(_crypto, _clientCert.CertificateStructure);
      var certificate = new Certificate(
        Array.Empty<byte>(), [new CertificateEntry(tlsCert, null)]);

      return new BcDefaultTlsCredentialedSigner(
        new TlsCryptoParameters(_context),
        _crypto,
        _clientKey,
        certificate,
        PinnedSignatureAlgorithm);
    }
  }
}
