using System.Threading.Channels;
using MessagePack;
using Microsoft.Extensions.Logging;
using Server.Streaming;
using Shared.Protocol;

namespace Server.Tunnel.Handlers;

internal static class LiveHandler
{
  public static async Task RunAsync(
    ChannelReader<MuxMessage> reader,
    IStreamSink sink,
    StreamTapRegistry tapRegistry,
    ILogger logger,
    CancellationToken ct)
  {
    var msg = await reader.ReadAsync(ct);
    var subscribe = MessagePackSerializer.Deserialize<LiveSubscribeMessage>(
      msg.Payload, ProtocolSerializer.Options);

    await StreamSessionRunner.RunLiveAsync(
      subscribe.CameraId, subscribe.Profile, sink, tapRegistry, logger, ct);
  }
}
