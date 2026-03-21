namespace Shared.Models;

public interface IVideoStream
{
  VideoStreamInfo Info { get; }
  ReadOnlyMemory<byte> Header { get; }
  Type FrameType { get; }
}

public interface IVideoStream<T> : IVideoStream where T : IDataUnit
{
  IAsyncEnumerable<T> ReadAsync(CancellationToken ct);
}
