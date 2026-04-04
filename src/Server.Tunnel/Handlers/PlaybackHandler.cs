using System.Threading.Channels;
using MessagePack;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Server.Streaming;
using Shared.Protocol;

namespace Server.Tunnel.Handlers;

internal static class PlaybackHandler
{
  public static async Task RunAsync(
    ChannelReader<MuxMessage> reader,
    IStreamSink sink,
    StreamTapRegistry tapRegistry,
    IPluginHost plugins,
    ILogger logger,
    CancellationToken ct)
  {
    var msg = await reader.ReadAsync(ct);
    var request = MessagePackSerializer.Deserialize<PlaybackRequestMessage>(
      msg.Payload, ProtocolSerializer.Options);

    await StreamSessionRunner.RunFetchAsync(
      request.CameraId, request.Profile,
      request.From, request.To ?? ulong.MaxValue,
      sink, tapRegistry, plugins, logger, ct);
  }
}
