namespace Client.Core.Tunnel;

public interface ITunnelService
{
  ConnectionState State { get; }
  event Action<ConnectionState>? StateChanged;
  uint Generation { get; }
  Task ConnectAsync(CancellationToken ct);
  Task DisconnectAsync();
  Task<MuxStream> OpenStreamAsync(ushort streamType, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
