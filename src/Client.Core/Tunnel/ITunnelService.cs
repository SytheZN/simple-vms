namespace Client.Core.Tunnel;

public sealed record ConnectionOptions(
  int LastSuccessfulIndex = -1,
  bool ReprobeEnabled = false);

public interface ITunnelService
{
  ConnectionState State { get; }
  int ConnectedAddressIndex { get; }
  event Action<ConnectionState>? StateChanged;
  uint Generation { get; }
  Task ConnectAsync(ConnectionOptions options, CancellationToken ct);
  Task DisconnectAsync(CancellationToken ct = default);
  Task<MuxStream> OpenStreamAsync(ushort streamType, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
