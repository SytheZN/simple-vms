using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Server.Core.PortForwarding;

namespace Tests.Unit.Core;

[TestFixture]
public class NatPmpClientTests
{
  /// <summary>
  /// SCENARIO:
  /// Router accepts the TCP mapping request and replies with the same external
  /// port and a non-zero lifetime
  ///
  /// ACTION:
  /// NatPmpClient.AddMappingAsync
  ///
  /// EXPECTED RESULT:
  /// The client sends a well-formed 12-byte request (version 0, opcode 2,
  /// internal port, external port, lifetime) and returns the parsed success
  /// response
  /// </summary>
  [Test]
  public async Task AddMappingAsync_SerializesRequestAndParsesSuccess()
  {
    using var listener = new UdpListener();
    listener.RespondWith(requestBytes =>
    {
      AssertRequest(requestBytes,
        expectedOpcode: 2,
        expectedInternalPort: 4433,
        expectedExternalPort: 34567,
        expectedLifetime: 3600);
      return BuildResponse(
        opcode: 2,
        code: NatPmpResultCode.Success,
        externalPort: 34567,
        lifetime: 3600);
    });

    var client = new NatPmpClient(port: listener.Port);
    var result = await client.AddMappingAsync(
      IPAddress.Loopback, internalPort: 4433, externalPort: 34567,
      lifetimeSeconds: 3600, CancellationToken.None);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.Code, Is.EqualTo(NatPmpResultCode.Success));
    Assert.That(result.ExternalPort, Is.EqualTo(34567));
    Assert.That(result.Lifetime, Is.EqualTo(3600u));
  }

  /// <summary>
  /// SCENARIO:
  /// Router responds with a non-success result code (e.g. NotAuthorized)
  ///
  /// ACTION:
  /// NatPmpClient.AddMappingAsync
  ///
  /// EXPECTED RESULT:
  /// Returns the response with the router's code surfaced so the caller can
  /// decide to fall back to UPnP
  /// </summary>
  [Test]
  public async Task AddMappingAsync_SurfacesNonSuccessCode()
  {
    using var listener = new UdpListener();
    listener.RespondWith(_ => BuildResponse(
      opcode: 2, code: NatPmpResultCode.NotAuthorized, externalPort: 0, lifetime: 0));

    var client = new NatPmpClient(port: listener.Port);
    var result = await client.AddMappingAsync(
      IPAddress.Loopback, 4433, 34567, 3600, CancellationToken.None);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.Code, Is.EqualTo(NatPmpResultCode.NotAuthorized));
  }

  /// <summary>
  /// SCENARIO:
  /// Nothing is listening on the router's NAT-PMP port
  ///
  /// ACTION:
  /// NatPmpClient.AddMappingAsync
  ///
  /// EXPECTED RESULT:
  /// Returns null after the response timeout - the caller treats null as
  /// "protocol unavailable, try UPnP"
  /// </summary>
  [Test]
  public async Task AddMappingAsync_ReturnsNullWhenNoRouterResponds()
  {
    var unusedPort = GetFreeUdpPort();
    var client = new NatPmpClient(port: unusedPort);

    var result = await client.AddMappingAsync(
      IPAddress.Loopback, 4433, 34567, 3600, CancellationToken.None);

    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// DeleteMappingAsync is a convenience wrapper that maps to AddMapping
  /// with externalPort=0 and lifetime=0 per RFC 6886
  ///
  /// ACTION:
  /// NatPmpClient.DeleteMappingAsync
  ///
  /// EXPECTED RESULT:
  /// The on-wire request encodes externalPort=0 and lifetime=0 while
  /// preserving the internal port
  /// </summary>
  [Test]
  public async Task DeleteMappingAsync_SendsZeroExternalPortAndLifetime()
  {
    using var listener = new UdpListener();
    byte[]? captured = null;
    listener.RespondWith(req =>
    {
      captured = req;
      return BuildResponse(2, NatPmpResultCode.Success, 0, 0);
    });

    var client = new NatPmpClient(port: listener.Port);
    await client.DeleteMappingAsync(IPAddress.Loopback, internalPort: 4433, CancellationToken.None);

    Assert.That(captured, Is.Not.Null);
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(captured.AsSpan(4)), Is.EqualTo(4433));
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(captured.AsSpan(6)), Is.EqualTo(0));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(captured.AsSpan(8)), Is.EqualTo(0u));
  }

  private static void AssertRequest(
    byte[] req, byte expectedOpcode,
    ushort expectedInternalPort, ushort expectedExternalPort, uint expectedLifetime)
  {
    Assert.That(req.Length, Is.EqualTo(12));
    Assert.That(req[0], Is.EqualTo(0), "version must be 0");
    Assert.That(req[1], Is.EqualTo(expectedOpcode));
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4)), Is.EqualTo(expectedInternalPort));
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(6)), Is.EqualTo(expectedExternalPort));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(req.AsSpan(8)), Is.EqualTo(expectedLifetime));
  }

  private static byte[] BuildResponse(
    byte opcode, NatPmpResultCode code, ushort externalPort, uint lifetime)
  {
    var buffer = new byte[16];
    buffer[0] = 0;
    buffer[1] = (byte)(opcode | 0x80);
    BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), (ushort)code);
    BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4), 0);
    BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(10), externalPort);
    BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12), lifetime);
    return buffer;
  }

  private static int GetFreeUdpPort()
  {
    using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
  }

  private sealed class UdpListener : IDisposable
  {
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    public int Port { get; }

    public UdpListener()
    {
      _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
      Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
    }

    public void RespondWith(Func<byte[], byte[]> responder)
    {
      _loop = Task.Run(async () =>
      {
        try
        {
          while (!_cts.IsCancellationRequested)
          {
            var received = await _udp.ReceiveAsync(_cts.Token);
            var reply = responder(received.Buffer);
            await _udp.SendAsync(reply, received.RemoteEndPoint, _cts.Token);
          }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
      });
    }

    public void Dispose()
    {
      _cts.Cancel();
      _udp.Dispose();
      try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
      _cts.Dispose();
    }
  }
}
