using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Server.Streaming;
using Shared.Protocol;

namespace Server.Tunnel.Handlers;

internal static class StreamCommandLoop
{
  public static async Task RunAsync(
    Guid cameraId,
    Func<CancellationToken, Task> initialOp,
    ChannelReader<MuxMessage> reader,
    IStreamSink sink,
    StreamTapRegistry tapRegistry,
    IPluginHost plugins,
    ILogger logger,
    CancellationToken ct)
  {
    var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var opTask = initialOp(opCts.Token);

    try
    {
      while (!ct.IsCancellationRequested)
      {
        MuxMessage msg;
        try { msg = await reader.ReadAsync(ct); }
        catch (ChannelClosedException) { break; }

        if (msg.Payload.Length == 0)
          continue;

        var data = msg.Payload.Span;
        var type = StreamMessageReader.ReadType(data);
        var live = type == ClientMessageType.Live ? StreamMessageReader.ReadLive(data) : default;
        var fetch = type == ClientMessageType.Fetch ? StreamMessageReader.ReadFetch(data) : default;

        await CancelAsync(opCts, opTask);
        opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        opTask = type switch
        {
          ClientMessageType.Live => StreamSessionRunner.RunLiveAsync(
            cameraId, live.Profile, sink, tapRegistry, logger, opCts.Token),
          ClientMessageType.Fetch => StreamSessionRunner.RunFetchAsync(
            cameraId, fetch.Profile, fetch.From, fetch.To, sink, tapRegistry, plugins,
            logger, opCts.Token),
          _ => Task.CompletedTask,
        };
      }
    }
    finally
    {
      await CancelAsync(opCts, opTask);
    }
  }

  private static async Task CancelAsync(CancellationTokenSource cts, Task task)
  {
    cts.Cancel();
    try { await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
    catch { }
    cts.Dispose();
  }
}
