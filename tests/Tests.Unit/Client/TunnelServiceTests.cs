using System.Buffers.Binary;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client;

[TestFixture]
public class TunnelServiceTests
{
  private static readonly CredentialData TestCreds = new(
    "ca", "cert", "key",
    ["127.0.0.1:9999", "10.0.0.1:9999"],
    Guid.NewGuid());

  /// <summary>
  /// SCENARIO:
  /// TunnelService is constructed with default state
  ///
  /// ACTION:
  /// Check initial state and generation
  ///
  /// EXPECTED RESULT:
  /// State is Disconnected, generation is 0
  /// </summary>
  [Test]
  public void InitialState_IsDisconnected()
  {
    var creds = new MockCredentialStore { Data = TestCreds };
    var transport = new MockTransportFactory();
    var service = new TunnelService(creds, transport, NullLogger<TunnelService>.Instance);

    Assert.That(service.State, Is.EqualTo(ConnectionState.Disconnected));
    Assert.That(service.Generation, Is.EqualTo(0u));
  }

  /// <summary>
  /// SCENARIO:
  /// ConnectAsync is called with a transport factory that returns a version-compatible stream
  ///
  /// ACTION:
  /// Connect and verify state
  ///
  /// EXPECTED RESULT:
  /// State transitions to Connected, generation increments to 1
  /// </summary>
  [Test]
  public async Task Connect_ValidServer_BecomesConnected()
  {
    var creds = new MockCredentialStore { Data = TestCreds };
    var transport = new MockTransportFactory();
    var service = new TunnelService(creds, transport, NullLogger<TunnelService>.Instance);

    var states = new List<ConnectionState>();
    service.StateChanged += s => states.Add(s);

    await service.ConnectAsync(new(), CancellationToken.None);

    Assert.That(service.State, Is.EqualTo(ConnectionState.Connected));
    Assert.That(service.Generation, Is.EqualTo(1u));
    Assert.That(states, Does.Contain(ConnectionState.Connecting));
    Assert.That(states, Does.Contain(ConnectionState.Connected));

    await service.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// ConnectAsync is called with no stored credentials
  ///
  /// ACTION:
  /// Attempt to connect
  ///
  /// EXPECTED RESULT:
  /// Throws InvalidOperationException
  /// </summary>
  [Test]
  public void Connect_NoCredentials_Throws()
  {
    var creds = new MockCredentialStore();
    var transport = new MockTransportFactory();
    var service = new TunnelService(creds, transport, NullLogger<TunnelService>.Instance);

    Assert.ThrowsAsync<InvalidOperationException>(
      () => service.ConnectAsync(new(), CancellationToken.None));
  }

  /// <summary>
  /// SCENARIO:
  /// The transport factory provides addresses in order
  ///
  /// ACTION:
  /// Connect with a factory where the first address fails
  ///
  /// EXPECTED RESULT:
  /// The second address is tried and succeeds
  /// </summary>
  [Test]
  public async Task Connect_FirstAddressFails_TriesSecond()
  {
    var creds = new MockCredentialStore { Data = TestCreds };
    var transport = new MockTransportFactory();
    transport.FailAddresses.Add("127.0.0.1:9999");
    var service = new TunnelService(creds, transport, NullLogger<TunnelService>.Instance);

    await service.ConnectAsync(new(), CancellationToken.None);

    Assert.That(service.State, Is.EqualTo(ConnectionState.Connected));
    Assert.That(transport.ConnectedAddress, Is.EqualTo("10.0.0.1:9999"));

    await service.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// The server responds with a mismatched protocol version
  ///
  /// ACTION:
  /// Attempt to connect
  ///
  /// EXPECTED RESULT:
  /// Throws InvalidOperationException with version mismatch message
  /// </summary>
  [Test]
  public void Connect_VersionMismatch_Throws()
  {
    var creds = new MockCredentialStore { Data = TestCreds };
    var transport = new MockTransportFactory { ServerVersion = 99 };
    var service = new TunnelService(creds, transport, NullLogger<TunnelService>.Instance);

    Assert.ThrowsAsync<InvalidOperationException>(
      () => service.ConnectAsync(new(), CancellationToken.None));
  }

  /// <summary>
  /// SCENARIO:
  /// Version exchange sends the correct bytes on the wire
  ///
  /// ACTION:
  /// Connect and inspect the bytes written to the transport
  ///
  /// EXPECTED RESULT:
  /// First frame is a mux header on stream 0 with a 4-byte payload containing version 1
  /// </summary>
  [Test]
  public async Task Connect_VersionExchange_WritesCorrectBytes()
  {
    var creds = new MockCredentialStore { Data = TestCreds };
    var transport = new MockTransportFactory();
    var service = new TunnelService(creds, transport, NullLogger<TunnelService>.Instance);

    await service.ConnectAsync(new(), CancellationToken.None);

    var written = transport.LastPipe!.WrittenBytes;
    Assert.That(written.Length, Is.GreaterThanOrEqualTo(MessageEnvelope.MuxHeaderSize + 4));

    var (streamId, flags, payloadLen) = MessageEnvelope.ReadMuxHeader(written);
    Assert.That(streamId, Is.EqualTo(0u));
    Assert.That(flags, Is.EqualTo((ushort)0));
    Assert.That(payloadLen, Is.EqualTo(4));

    var version = BinaryPrimitives.ReadUInt32LittleEndian(
      written.AsSpan(MessageEnvelope.MuxHeaderSize));
    Assert.That(version, Is.EqualTo(1u));

    await service.DisconnectAsync();
  }

  internal sealed class MockTransportFactory : ITransportFactory
  {
    public HashSet<string> FailAddresses { get; } = [];
    public string? ConnectedAddress { get; private set; }
    public uint ServerVersion { get; set; } = 1;
    public VersionExchangePipe? LastPipe { get; private set; }

    public Task<TransportConnection> ConnectAsync(
      string address, CredentialData creds, CancellationToken ct)
    {
      if (FailAddresses.Contains(address))
        throw new IOException($"Connection refused: {address}");

      ConnectedAddress = address;
      var pipe = new VersionExchangePipe(ServerVersion);
      LastPipe = pipe;
      return Task.FromResult(new TransportConnection(pipe));
    }
  }

  internal sealed class VersionExchangePipe : Stream
  {
    private readonly MemoryStream _written = new();
    private readonly MemoryStream _toRead = new();
    private bool _versionSent;
    private readonly uint _serverVersion;

    public byte[] WrittenBytes => _written.ToArray();

    public VersionExchangePipe(uint serverVersion)
    {
      _serverVersion = serverVersion;
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

    public override void Write(byte[] buffer, int offset, int count) =>
      _written.Write(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
      _written.Write(buffer.Span);

      if (!_versionSent && _written.Length >= MessageEnvelope.MuxHeaderSize + 4)
      {
        _versionSent = true;
        var response = new byte[MessageEnvelope.MuxHeaderSize + 4];
        MessageEnvelope.WriteMuxHeader(response, 0, 0, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(
          response.AsSpan(MessageEnvelope.MuxHeaderSize), _serverVersion);
        _toRead.Write(response);
        _toRead.Position = 0;
      }

      return ValueTask.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
      _toRead.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
      if (_toRead.Position >= _toRead.Length)
        return new ValueTask<int>(Task.Delay(Timeout.Infinite, ct).ContinueWith<int>(_ => 0));

      var read = _toRead.Read(buffer.Span);
      return new ValueTask<int>(read);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
  }
}
