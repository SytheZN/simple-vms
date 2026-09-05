using System.Diagnostics.CodeAnalysis;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x.Filters;

public sealed class Despeckle : IFilter
{
  private const int Prev1Radius = 2;
  private const int Prev2Radius = 4;
  private const int SupportShift = 1;
  private const int HistoryFrames = 2;

  private byte[] _prev1 = [];
  private byte[] _prev2 = [];
  private int _framesSeen;
  private MotionGridUnit? _ready;

  public void Feed(MotionGridUnit unit)
  {
    int width = unit.Width, height = unit.Height;
    var cells = width * height;
    var incoming = unit.Data.Span;
    var cleaned = new byte[cells];

    if (_prev1.Length == 0)
    {
      _prev1 = new byte[cells];
      _prev2 = new byte[cells];
    }

    if (_framesSeen >= HistoryFrames)
    {
      for (var y = 0; y < height; y++)
      {
        for (var x = 0; x < width; x++)
        {
          var i = y * width + x;
          var value = incoming[i];
          if (value == 0) continue;
          var required = Math.Max(1, value >> SupportShift);
          if (!HasSupport(_prev1, x, y, width, height, Prev1Radius, required)) continue;
          if (!HasSupport(_prev2, x, y, width, height, Prev2Radius, required)) continue;
          cleaned[i] = value;
        }
      }
    }

    (_prev2, _prev1) = (_prev1, _prev2);
    incoming.CopyTo(_prev1);
    if (_framesSeen < HistoryFrames) _framesSeen++;

    _ready = new MotionGridUnit
    {
      Data = cleaned,
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

  public void Flush() { }

  private static bool HasSupport(
    ReadOnlySpan<byte> cells, int x, int y, int width, int height, int radius, int required)
  {
    var left = Math.Max(0, x - radius);
    var right = Math.Min(width - 1, x + radius);
    var top = Math.Max(0, y - radius);
    var bottom = Math.Min(height - 1, y + radius);

    for (var ny = top; ny <= bottom; ny++)
    {
      for (var nx = left; nx <= right; nx++)
      {
        if (cells[ny * width + nx] >= required) return true;
      }
    }
    return false;
  }
}
