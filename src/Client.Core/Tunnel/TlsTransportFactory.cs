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
        await tcpClient.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
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
        await DriveHandshakeAsync(protocol, tlsClient, netStream, ct).ConfigureAwait(false);
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

      return new TransportConnection(new NonBlockingTlsStream(protocol, netStream, _logger), tcpClient);
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
        await FlushOutputAsync(protocol, net, ct).ConfigureAwait(false);
        if (tlsClient.HandshakeComplete) break;

        var n = await net.ReadAsync(ioBuf.AsMemory(0, 16384), ct).ConfigureAwait(false);
        if (n <= 0)
          throw new IOException("Connection closed during TLS handshake");
        protocol.OfferInput(ioBuf, 0, n);
      }
      await FlushOutputAsync(protocol, net, ct).ConfigureAwait(false);
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
        await net.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
    }
    finally { ArrayPool<byte>.Shared.Return(buf); }
  }

  private sealed class NonBlockingTlsStream : Stream
  {
    private static readonly TimeSpan CloseLockTimeout = TimeSpan.FromSeconds(2);

    private readonly TlsClientProtocol _protocol;
    private readonly Stream _transport;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _protocolLock = new(1, 1);
    private readonly SemaphoreSlim _socketReadLock = new(1, 1);
    private bool _disposed;

    public NonBlockingTlsStream(TlsClientProtocol protocol, Stream transport, ILogger logger)
    {
      _protocol = protocol;
      _transport = transport;
      _logger = logger;
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

    private const int TransportReadBufferBytes = 16384;

    public override async Task<int> ReadAsync(
      byte[] buffer, int offset, int count, CancellationToken ct)
    {
      byte[]? rbuf = null;
      try
      {
        while (true)
        {
          ct.ThrowIfCancellationRequested();

          var consumed = await DrainDecryptedPlaintextInto(buffer, offset, count, ct)
            .ConfigureAwait(false);
          if (consumed > 0) return consumed;

          rbuf ??= ArrayPool<byte>.Shared.Rent(TransportReadBufferBytes);
          var bytesRead = await ReadCiphertextAndOfferInSequence(rbuf, ct).ConfigureAwait(false);

          if (bytesRead <= 0)
          {
            return await DrainDecryptedPlaintextInto(buffer, offset, count, ct)
              .ConfigureAwait(false);
          }
        }
      }
      finally
      {
        if (rbuf != null)
          ArrayPool<byte>.Shared.Return(rbuf);
      }
    }

    private async Task<int> DrainDecryptedPlaintextInto(
      byte[] buffer, int offset, int count, CancellationToken ct)
    {
      await _protocolLock.WaitAsync(ct).ConfigureAwait(false);
      try
      {
        var available = _protocol.GetAvailableInputBytes();
        if (available <= 0) return 0;
        return _protocol.ReadInput(buffer, offset, Math.Min(available, count));
      }
      finally { _protocolLock.Release(); }
    }

    private async Task<int> ReadCiphertextAndOfferInSequence(byte[] rbuf, CancellationToken ct)
    {
      await _socketReadLock.WaitAsync(ct).ConfigureAwait(false);
      try
      {
        var bytesRead = await _transport.ReadAsync(rbuf.AsMemory(0, TransportReadBufferBytes), ct)
          .ConfigureAwait(false);
        if (bytesRead > 0)
          await OfferCiphertextAndSendResponse(rbuf, bytesRead, ct).ConfigureAwait(false);
        return bytesRead;
      }
      finally { _socketReadLock.Release(); }
    }

    private async Task OfferCiphertextAndSendResponse(byte[] rbuf, int length, CancellationToken ct)
    {
      await _protocolLock.WaitAsync(ct).ConfigureAwait(false);
      try
      {
        _protocol.OfferInput(rbuf, 0, length);
        await SendPendingProtocolOutput(ct).ConfigureAwait(false);
      }
      finally { _protocolLock.Release(); }
    }

    public override async Task WriteAsync(
      byte[] buffer, int offset, int count, CancellationToken ct)
    {
      await _protocolLock.WaitAsync(ct).ConfigureAwait(false);
      try
      {
        _protocol.WriteApplicationData(buffer, offset, count);
        await SendPendingProtocolOutput(ct).ConfigureAwait(false);
      }
      finally { _protocolLock.Release(); }
    }

    private async Task SendPendingProtocolOutput(CancellationToken ct)
    {
      var avail = _protocol.GetAvailableOutputBytes();
      if (avail <= 0) return;

      var outBytes = ArrayPool<byte>.Shared.Rent(avail);
      try
      {
        var outLen = _protocol.ReadOutput(outBytes, 0, avail);
        await _transport.WriteAsync(outBytes.AsMemory(0, outLen), ct).ConfigureAwait(false);
      }
      finally { ArrayPool<byte>.Shared.Return(outBytes); }
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
        var n = await ReadAsync(rented, 0, dest.Length, ct).ConfigureAwait(false);
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
      try { await WriteAsync(rented, 0, len, ct).ConfigureAwait(false); }
      finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    protected override void Dispose(bool disposing)
    {
      if (_disposed) return;
      _disposed = true;
      if (disposing) SendCloseNotifyIfProtocolIdle();
      base.Dispose(disposing);
    }

    private void SendCloseNotifyIfProtocolIdle()
    {
      var locked = _protocolLock.Wait(CloseLockTimeout);
      try
      {
        if (locked)
          _protocol.Close();
        else
          _logger.LogDebug("TLS close_notify skipped: protocol is still in use");
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "TlsClientProtocol.Close failed");
      }
      finally
      {
        if (locked) _protocolLock.Release();
      }
    }
  }

  private static readonly SignatureAndHashAlgorithm PinnedSignatureAlgorithm =
    new(HashAlgorithm.Intrinsic, SignatureAlgorithm.rsa_pss_rsae_sha256);

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

      try
      {
        var leaf = ParseLeafCertificate(chain);
        RejectIfNotIssuedByPinnedCa(leaf);
        RejectIfNotUsableForServerAuth(leaf);
      }
      catch (Exception ex)
      {
        throw new TlsFatalAlert(AlertDescription.bad_certificate, ex);
      }
    }

    private static Org.BouncyCastle.X509.X509Certificate ParseLeafCertificate(Certificate chain)
    {
      var leafEncoded = chain.GetCertificateAt(0).GetEncoded();
      return new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(leafEncoded);
    }

    private void RejectIfNotIssuedByPinnedCa(Org.BouncyCastle.X509.X509Certificate leaf)
    {
      leaf.Verify(_caCert.GetPublicKey());
      leaf.CheckValidity();
    }

    private static void RejectIfNotUsableForServerAuth(Org.BouncyCastle.X509.X509Certificate leaf)
    {
      var ekus = leaf.GetExtendedKeyUsage();
      if (ekus == null || !ekus.Contains(KeyPurposeID.id_kp_serverAuth))
        throw new InvalidOperationException("Server cert missing serverAuth EKU");
    }

    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
      RejectIfServerRejectsPinnedSignatureScheme(certificateRequest);

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

    private static void RejectIfServerRejectsPinnedSignatureScheme(CertificateRequest? request)
    {
      var serverAlgs = request?.SupportedSignatureAlgorithms;
      if (serverAlgs == null) return;
      if (serverAlgs.Any(a =>
            a.Hash == PinnedSignatureAlgorithm.Hash &&
            a.Signature == PinnedSignatureAlgorithm.Signature))
        return;

      throw new TlsFatalAlert(AlertDescription.handshake_failure,
        new InvalidOperationException("Server does not accept pinned signature scheme"));
    }
  }
}
