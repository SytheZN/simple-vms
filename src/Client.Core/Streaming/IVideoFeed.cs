using Shared.Protocol;

namespace Client.Core.Streaming;

public interface IVideoFeed : IAsyncDisposable
{
  Guid CameraId { get; }
  string Profile { get; }
  ReadOnlyMemory<byte> LastInit { get; }

  event Action<ReadOnlyMemory<byte>>? OnInit;
  event Action<GopMessage>? OnGop;
  event Action<StreamStatus>? OnStatus;
  event Action<GapStatus>? OnGap;
  event Action? OnCompleted;

  void Start();

  Task SendFetchAsync(ulong from, ulong to, CancellationToken ct);
}
