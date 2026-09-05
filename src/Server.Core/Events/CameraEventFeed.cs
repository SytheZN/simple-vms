using System.Threading.Channels;
using Shared.Models;
using Shared.Models.Events;
using Shared.Protocol;

namespace Server.Core.Events;

public static class CameraEventFeed
{
  private const int QueueDepth = 256;

  public static async Task RunAsync(
    Func<EventChannelMessage, EventChannelFlags, CancellationToken, Task> writeAsync,
    IEventBus eventBus,
    CancellationToken ct)
  {
    var queue = Channel.CreateBounded<(EventChannelMessage Message, EventChannelFlags Flags)>(
      new BoundedChannelOptions(QueueDepth)
      {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
      });

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var token = cts.Token;

    var subscriptions = new[]
    {
      Subscribe<CameraEventRecorded>(eventBus, queue.Writer, token, evt =>
        (new EventChannelMessage
        {
          Id = evt.Id,
          CameraId = evt.CameraId,
          Type = evt.Type,
          StartTime = evt.Timestamp,
          EndTime = evt.EndTime,
          Metadata = evt.Metadata
        },
        evt.Ended ? EventChannelFlags.End : EventChannelFlags.Start)),
      Subscribe<CameraStatusChanged>(eventBus, queue.Writer, token, evt =>
      {
        var metadata = new Dictionary<string, string>
        {
          ["status"] = evt.Status,
          ["profile"] = evt.Profile
        };
        if (evt.Reason != null)
          metadata["reason"] = evt.Reason;
        return (Build(evt.CameraId, "__status", evt.Timestamp, metadata), EventChannelFlags.Start);
      }),
      Subscribe<CameraRemoved>(eventBus, queue.Writer, token, evt =>
        (Build(evt.CameraId, "__removed", evt.Timestamp), EventChannelFlags.Start)),
      Subscribe<CameraRecordingChanged>(eventBus, queue.Writer, token, evt =>
      {
        var metadata = new Dictionary<string, string>
        {
          ["state"] = evt.State.ToString().ToLowerInvariant(),
          ["profile"] = evt.Profile
        };
        return (Build(evt.CameraId, "__recording", evt.Timestamp, metadata), EventChannelFlags.Start);
      }),
      Subscribe<SystemEventRecorded>(eventBus, queue.Writer, token, evt =>
      {
        var metadata = evt.Metadata != null
          ? new Dictionary<string, string>(evt.Metadata)
          : new Dictionary<string, string>();
        metadata["source"] = evt.Source;
        return (new EventChannelMessage
        {
          Id = evt.Id,
          CameraId = Guid.Empty,
          Type = evt.Type,
          StartTime = evt.Timestamp,
          Metadata = metadata
        }, EventChannelFlags.Start);
      })
    };

    try
    {
      while (true)
      {
        bool available;
        try { available = await queue.Reader.WaitToReadAsync(token); }
        catch (OperationCanceledException) { break; }
        if (!available) break;
        while (queue.Reader.TryRead(out var item))
          await writeAsync(item.Message, item.Flags, token);
      }
    }
    finally
    {
      cts.Cancel();
      foreach (var subscription in subscriptions)
        await subscription.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
  }

  private static EventChannelMessage Build(
    Guid cameraId, string type, ulong timestamp,
    Dictionary<string, string>? metadata = null) =>
    new()
    {
      Id = Guid.NewGuid(),
      CameraId = cameraId,
      Type = type,
      StartTime = timestamp,
      Metadata = metadata
    };

  private static Task Subscribe<T>(
    IEventBus eventBus,
    ChannelWriter<(EventChannelMessage, EventChannelFlags)> writer,
    CancellationToken ct,
    Func<T, (EventChannelMessage, EventChannelFlags)> project) where T : ISystemEvent =>
    Task.Run(async () =>
    {
      await foreach (var evt in eventBus.SubscribeAsync<T>(ct))
        await writer.WriteAsync(project(evt), ct);
    }, ct);
}
