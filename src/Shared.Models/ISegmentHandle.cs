namespace Shared.Models;

public interface ISegmentHandle : IAsyncDisposable
{
  string SegmentRef { get; }
  Stream Stream { get; }
  Task FinalizeAsync(CancellationToken ct);
}
