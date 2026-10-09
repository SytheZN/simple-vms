namespace Shared.Models;

public interface ISegmentHandle : IAsyncDisposable
{
  string SegmentRef { get; }
  Stream Stream { get; }
  Task<OneOf<Success, Error>> FinalizeAsync(CancellationToken ct);
}
