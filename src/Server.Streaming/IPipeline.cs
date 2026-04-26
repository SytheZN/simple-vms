using System.Diagnostics.CodeAnalysis;
using Shared.Models;

namespace Server.Streaming;

public interface IPipeline : IAsyncDisposable
{
  Guid CameraId { get; }
  string Profile { get; }
  bool IsConstructed { get; }
  ReadOnlyMemory<byte> MuxHeader { get; }
  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  Task<OneOf<Success, Error>> ConstructAsync(CancellationToken ct);
  Task<OneOf<IDataStream, Error>> SubscribeDataAsync(CancellationToken ct);
  Task<OneOf<IMuxStream, Error>> SubscribeMuxAsync(CancellationToken ct);
}
