using System.Diagnostics.CodeAnalysis;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x.Filters;

public sealed class TemporalDeblock : IFilter
{
  private const int Radius = 3;
  private const int SupportArea = 9;
  private const int StrengthNumerator = 3;
  private const int StrengthDenominator = 4;

  private int[] _previousIntegral = [];
  private int[] _heldIntegral = [];
  private int[] _spareIntegral = [];
  private bool _hasPrevious;
  private MotionGridUnit? _held;
  private readonly Queue<MotionGridUnit> _ready = new();

  public void Feed(MotionGridUnit unit)
  {
    var incoming = BuildIntegral(unit.Data.Span, unit.Width, unit.Height, _spareIntegral);
    Release(incoming);
    _spareIntegral = _previousIntegral;
    _previousIntegral = _heldIntegral;
    _hasPrevious = _held != null;
    _heldIntegral = incoming;
    _held = unit;
  }

  public bool TryReceive([MaybeNullWhen(false)] out MotionGridUnit unit) =>
    _ready.TryDequeue(out unit);

  public void Flush()
  {
    Release(null);
    _held = null;
    _hasPrevious = false;
  }

  private void Release(int[]? nextIntegral)
  {
    if (_held == null) return;

    int width = _held.Width, height = _held.Height;
    var incoming = _held.Data.Span;
    var cleaned = new byte[width * height];
    var stride = width + 1;
    var length = stride * (height + 1);
    var previous = _hasPrevious && _previousIntegral.Length == length ? _previousIntegral : null;
    var next = nextIntegral != null && nextIntegral.Length == length ? nextIntegral : null;

    for (var y = 0; y < height; y++)
    {
      var top = Math.Max(0, y - Radius);
      var bottom = Math.Min(height - 1, y + Radius);
      for (var x = 0; x < width; x++)
      {
        var value = incoming[y * width + x];
        if (value == 0) continue;
        var required = value * SupportArea * StrengthNumerator;
        var left = Math.Max(0, x - Radius);
        var right = Math.Min(width - 1, x + Radius);
        if (Aggregate(previous, stride, left, right, top, bottom) * StrengthDenominator >= required
          || Aggregate(next, stride, left, right, top, bottom) * StrengthDenominator >= required)
        {
          cleaned[y * width + x] = value;
        }
      }
    }

    _ready.Enqueue(new MotionGridUnit
    {
      Data = cleaned,
      Timestamp = _held.Timestamp,
      IsSyncPoint = _held.IsSyncPoint,
      Width = _held.Width,
      Height = _held.Height
    });
  }

  private static int Aggregate(int[]? integral, int stride, int left, int right, int top, int bottom)
  {
    if (integral == null) return 0;
    var rowBottom = (bottom + 1) * stride;
    var rowTop = top * stride;
    return integral[rowBottom + right + 1] - integral[rowTop + right + 1]
      - integral[rowBottom + left] + integral[rowTop + left];
  }

  private static int[] BuildIntegral(ReadOnlySpan<byte> cells, int width, int height, int[] reuse)
  {
    var stride = width + 1;
    var length = stride * (height + 1);
    var integral = reuse.Length == length ? reuse : new int[length];
    for (var x = 0; x <= width; x++) integral[x] = 0;
    for (var y = 0; y < height; y++)
    {
      var rowSum = 0;
      var row = (y + 1) * stride;
      var above = y * stride;
      integral[row] = 0;
      for (var x = 0; x < width; x++)
      {
        rowSum += cells[y * width + x];
        integral[row + x + 1] = integral[above + x + 1] + rowSum;
      }
    }
    return integral;
  }
}
