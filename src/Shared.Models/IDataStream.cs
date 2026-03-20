namespace Shared.Models;

public interface IDataStream
{
  StreamInfo Info { get; }
  Type FrameType { get; }
}

public interface IDataStream<T> : IDataStream where T : IDataUnit
{
  IAsyncEnumerable<T> ReadAsync(CancellationToken ct);
}
