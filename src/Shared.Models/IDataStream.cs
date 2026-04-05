namespace Shared.Models;

public interface IDataStream
{
  StreamInfo Info { get; }
  Type FrameType { get; }
  IAsyncEnumerable<IDataUnit> ReadAsync(CancellationToken ct);
}

public interface IDataStream<out T> : IDataStream where T : IDataUnit
{
  new IAsyncEnumerable<T> ReadAsync(CancellationToken ct);

  IAsyncEnumerable<IDataUnit> IDataStream.ReadAsync(CancellationToken ct) =>
    ReadAsDataUnits(ct);

  private async IAsyncEnumerable<IDataUnit> ReadAsDataUnits(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var item in ReadAsync(ct).WithCancellation(ct))
      yield return item;
  }
}
