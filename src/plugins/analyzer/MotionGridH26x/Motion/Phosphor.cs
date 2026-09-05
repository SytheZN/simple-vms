using System.Diagnostics.CodeAnalysis;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x;

internal sealed class Phosphor : IMotionGridAlgorithm
{
  private readonly int _step;
  private byte[] _state = [];
  private int _cells;
  private MotionGridUnit? _ready;

  public Phosphor(int window)
  {
    _step = Math.Max(1, (255 + window - 1) / window);
  }

  public void Feed(MotionGridUnit unit)
  {
    var cells = unit.Width * unit.Height;

    if (cells != _cells)
    {
      _state = new byte[cells];
      _cells = cells;
    }

    var step = _step;
    var incoming = unit.Data.Span;
    for (var i = 0; i < _cells; i++)
    {
      var decayed = Math.Max(0, _state[i] - step);
      _state[i] = Math.Max(incoming[i], (byte)decayed);
    }

    _ready = new MotionGridUnit
    {
      Data = _state.ToArray(),
      Timestamp = unit.Timestamp,
      IsSyncPoint = unit.IsSyncPoint,
      Width = unit.Width,
      Height = unit.Height
    };
  }

  public bool TryReceive([MaybeNullWhen(false)] out MotionGridUnit unit)
  {
    unit = _ready;
    _ready = null;
    return unit != null;
  }

  public void Flush()
  {
    _state = [];
    _cells = 0;
    _ready = null;
  }
}
