using MessagePack;
using Server.Core.Events;
using Shared.Models;
using Shared.Protocol;

namespace Server.Tunnel.Handlers;

internal static class EventChannelHandler
{
  public static Task RunAsync(
    Func<ushort, ReadOnlyMemory<byte>, CancellationToken, Task> writeFn,
    IEventBus eventBus,
    CancellationToken ct) =>
    CameraEventFeed.RunAsync(
      (message, flags, token) => writeFn(
        (ushort)flags,
        MessagePackSerializer.Serialize(message, ProtocolSerializer.Options),
        token),
      eventBus, ct);
}
