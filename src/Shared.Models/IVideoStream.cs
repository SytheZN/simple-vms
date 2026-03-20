namespace Shared.Models;

public interface IVideoStream
{
  StreamInfo Info { get; }
  Type FrameType { get; }
}

public interface IVideoStream<T> : IVideoStream where T : IDataUnit
{
  IAsyncEnumerable<T> ReadAsync(CancellationToken ct);
}
