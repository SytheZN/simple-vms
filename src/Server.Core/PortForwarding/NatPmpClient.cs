using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Server.Core.PortForwarding;

public enum NatPmpResultCode : ushort
{
  Success = 0,
  UnsupportedVersion = 1,
  NotAuthorized = 2,
  NetworkFailure = 3,
  OutOfResources = 4,
  UnsupportedOpcode = 5
}

public sealed record NatPmpMapResult(NatPmpResultCode Code, ushort ExternalPort, uint Lifetime);

public sealed class NatPmpClient
{
  private const byte NatPmpVersion = 0;
  private const byte OpMapTcp = 2;
  private const int DefaultNatPmpPort = 5351;
  private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(2);

  private readonly int _port;

  public NatPmpClient(int port = DefaultNatPmpPort)
  {
    _port = port;
  }

  public async Task<NatPmpMapResult?> AddMappingAsync(
    IPAddress router, ushort internalPort, ushort externalPort,
    uint lifetimeSeconds, CancellationToken ct)
  {
    using var udp = new UdpClient(AddressFamily.InterNetwork);
    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

    var request = new byte[12];
    request[0] = NatPmpVersion;
    request[1] = OpMapTcp;
    BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), internalPort);
    BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), externalPort);
    BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8), lifetimeSeconds);

    try
    {
      await udp.SendAsync(request, new IPEndPoint(router, _port), ct);
    }
    catch
    {
      return null;
    }

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(ResponseTimeout);

    try
    {
      while (!timeout.IsCancellationRequested)
      {
        var result = await udp.ReceiveAsync(timeout.Token);
        if (!result.RemoteEndPoint.Address.Equals(router)) continue;
        var response = result.Buffer;
        if (response.Length < 16) continue;
        if (response[0] != NatPmpVersion) continue;
        if (response[1] != (OpMapTcp | 0x80)) continue;

        var code = (NatPmpResultCode)BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2));
        var mappedExternal = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(10));
        var actualLifetime = BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(12));
        return new NatPmpMapResult(code, mappedExternal, actualLifetime);
      }
    }
    catch (OperationCanceledException) { }

    return null;
  }

  public Task<NatPmpMapResult?> DeleteMappingAsync(
    IPAddress router, ushort internalPort, CancellationToken ct) =>
    AddMappingAsync(router, internalPort, externalPort: 0, lifetimeSeconds: 0, ct);
}
