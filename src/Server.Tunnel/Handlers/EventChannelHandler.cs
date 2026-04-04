using System.Threading.Channels;
using MessagePack;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Events;
using Shared.Protocol;

namespace Server.Tunnel.Handlers;

internal static class EventChannelHandler
{
  public static async Task RunAsync(
    ChannelReader<MuxMessage> reader,
    Func<ushort, ReadOnlyMemory<byte>, CancellationToken, Task> writeFn,
    IEventBus eventBus,
    ILogger logger,
    CancellationToken ct)
  {
    var eventChannel = Channel.CreateBounded<(EventChannelMessage Message, EventChannelFlags Flags)>(
      new BoundedChannelOptions(256)
      {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
      });

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var token = cts.Token;

    var subscriptions = new[]
    {
      SubscribeCameraStatus(eventBus, eventChannel.Writer, token),
      SubscribeMotionDetected(eventBus, eventChannel.Writer, token),
      SubscribeMotionEnded(eventBus, eventChannel.Writer, token),
      SubscribeOnvifEvent(eventBus, eventChannel.Writer, token)
    };

    try
    {
      await foreach (var (msg, flags) in eventChannel.Reader.ReadAllAsync(token))
      {
        var payload = MessagePackSerializer.Serialize(msg, ProtocolSerializer.Options);
        await writeFn((ushort)flags, payload, token);
      }
    }
    finally
    {
      cts.Cancel();
      foreach (var sub in subscriptions)
      {
        try { await sub; }
        catch (OperationCanceledException) { }
      }
    }
  }

  private static Task SubscribeCameraStatus(
    IEventBus eventBus,
    ChannelWriter<(EventChannelMessage, EventChannelFlags)> writer,
    CancellationToken ct) =>
    Task.Run(async () =>
    {
      await foreach (var evt in eventBus.SubscribeAsync<CameraStatusChanged>(ct))
      {
        var msg = new EventChannelMessage
        {
          Id = Guid.NewGuid(),
          CameraId = evt.CameraId,
          Type = "status",
          StartTime = evt.Timestamp,
          Metadata = new Dictionary<string, string>
          {
            ["status"] = evt.Status,
            ["profile"] = evt.Profile
          }
        };
        if (evt.Reason != null)
          msg.Metadata["reason"] = evt.Reason;

        await writer.WriteAsync((msg, EventChannelFlags.Start), ct);
      }
    }, ct);

  private static Task SubscribeMotionDetected(
    IEventBus eventBus,
    ChannelWriter<(EventChannelMessage, EventChannelFlags)> writer,
    CancellationToken ct) =>
    Task.Run(async () =>
    {
      await foreach (var evt in eventBus.SubscribeAsync<MotionDetected>(ct))
      {
        var msg = new EventChannelMessage
        {
          Id = Guid.NewGuid(),
          CameraId = evt.CameraId,
          Type = "motion",
          StartTime = evt.Timestamp
        };
        await writer.WriteAsync((msg, EventChannelFlags.Start), ct);
      }
    }, ct);

  private static Task SubscribeMotionEnded(
    IEventBus eventBus,
    ChannelWriter<(EventChannelMessage, EventChannelFlags)> writer,
    CancellationToken ct) =>
    Task.Run(async () =>
    {
      await foreach (var evt in eventBus.SubscribeAsync<MotionEnded>(ct))
      {
        var msg = new EventChannelMessage
        {
          Id = Guid.NewGuid(),
          CameraId = evt.CameraId,
          Type = "motion",
          StartTime = evt.Timestamp
        };
        await writer.WriteAsync((msg, EventChannelFlags.End), ct);
      }
    }, ct);

  private static Task SubscribeOnvifEvent(
    IEventBus eventBus,
    ChannelWriter<(EventChannelMessage, EventChannelFlags)> writer,
    CancellationToken ct) =>
    Task.Run(async () =>
    {
      await foreach (var evt in eventBus.SubscribeAsync<OnvifEvent>(ct))
      {
        var msg = new EventChannelMessage
        {
          Id = Guid.NewGuid(),
          CameraId = evt.CameraId,
          Type = evt.Topic,
          StartTime = evt.Timestamp,
          Metadata = evt.Data != null
            ? new Dictionary<string, string>(evt.Data)
            : null
        };
        await writer.WriteAsync((msg, EventChannelFlags.Start), ct);
      }
    }, ct);
}
