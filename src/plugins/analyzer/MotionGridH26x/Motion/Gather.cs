using System.Diagnostics.CodeAnalysis;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x;

internal sealed class Gather : IMotionGridAlgorithm
{
  private readonly byte[][] _ring;
  private int _cursor;
  private int _count;
  private int _cells;
  private MotionGridUnit? _template;
  private MotionGridUnit? _ready;

  public Gather(int window)
  {
    _ring = new byte[window][];
  }

  public void Feed(MotionGridUnit unit)
  {
    if (_cells == 0)
      _cells = unit.Width * unit.Height;

    var slot = _ring[_cursor] ??= new byte[_cells];

    unit.Data.Span.CopyTo(slot);
    _template = unit;
    _cursor = (_cursor + 1) % _ring.Length;
    if (_count < _ring.Length) _count++;

    Emit();
  }

  public bool TryReceive([MaybeNullWhen(false)] out MotionGridUnit unit)
  {
    unit = _ready;
    _ready = null;
    return unit != null;
  }

  public void Flush()
  {
    _count = 0;
    _cursor = 0;
    _cells = 0;
    _template = null;
  }

  private void Emit()
  {
    if (_template == null || _count == 0) return;

    var output = new byte[_cells];
    for (var i = 0; i < _count; i++)
    {
      var frame = _ring[i];
      for (var c = 0; c < _cells; c++)
      {
        if (frame[c] > output[c])
          output[c] = frame[c];
      }
    }

    _ready = new MotionGridUnit
    {
      Data = output,
      Timestamp = _template.Timestamp,
      IsSyncPoint = _template.IsSyncPoint,
      Width = _template.Width,
      Height = _template.Height
    };
  }
}
