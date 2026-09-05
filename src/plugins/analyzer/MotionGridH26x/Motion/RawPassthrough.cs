using System.Diagnostics.CodeAnalysis;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x;

internal sealed class RawPassthrough : IMotionGridAlgorithm
{
  private readonly Queue<MotionGridUnit> _ready = new();

  public void Feed(MotionGridUnit unit) => _ready.Enqueue(unit);

  public bool TryReceive([MaybeNullWhen(false)] out MotionGridUnit unit) =>
    _ready.TryDequeue(out unit);

  public void Flush() { }
}
