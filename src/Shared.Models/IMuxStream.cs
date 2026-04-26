namespace Shared.Models;

public interface IMuxStream
{
  MuxStreamInfo Info { get; }
  ReadOnlyMemory<byte> Header { get; }
  Type FrameType { get; }
  IAsyncEnumerable<IDataUnit> ReadAsync(CancellationToken ct);
}

public interface IMuxStream<out T> : IMuxStream where T : IDataUnit
{
  new IAsyncEnumerable<T> ReadAsync(CancellationToken ct);

  IAsyncEnumerable<IDataUnit> IMuxStream.ReadAsync(CancellationToken ct) =>
    ReadAsDataUnits(ct);

  private async IAsyncEnumerable<IDataUnit> ReadAsDataUnits(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var item in ReadAsync(ct).WithCancellation(ct))
      yield return item;
  }
}
