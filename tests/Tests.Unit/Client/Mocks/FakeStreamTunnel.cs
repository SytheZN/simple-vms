using System.Threading.Channels;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;

namespace Tests.Unit.Client.Mocks;

public sealed class FakeStreamTunnel : ITunnelService
{
  public ConnectionState State { get; set; } = ConnectionState.Connected;
#pragma warning disable CS0067
  public event Action<ConnectionState>? StateChanged;
#pragma warning restore CS0067
  public uint Generation => 1;
  public int OpenCount { get; private set; }
  public byte[]? LastPayload { get; private set; }
  public Channel<MuxMessage>? LastChannel { get; private set; }

  public Task ConnectAsync(CancellationToken ct)
  {
    State = ConnectionState.Connected;
    StateChanged?.Invoke(State);
    return Task.CompletedTask;
  }

  public Task DisconnectAsync()
  {
    State = ConnectionState.Disconnected;
    StateChanged?.Invoke(State);
    return Task.CompletedTask;
  }

  public Task<MuxStream> OpenStreamAsync(
    ushort streamType, ReadOnlyMemory<byte> payload, CancellationToken ct)
  {
    OpenCount++;
    LastPayload = payload.ToArray();
    var channel = Channel.CreateUnbounded<MuxMessage>();
    LastChannel = channel;

    var transport = new MemoryStream();
    var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);
    var stream = new MuxStream(muxer, 1, channel.Reader);
    return Task.FromResult(stream);
  }

  public void FireStateChanged(ConnectionState state)
  {
    State = state;
    StateChanged?.Invoke(state);
  }
}
