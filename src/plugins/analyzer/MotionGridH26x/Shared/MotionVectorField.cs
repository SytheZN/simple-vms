namespace Analyzer.MotionGridH26x;

public sealed class MotionVectorField
{
  private int[] _current = [];
  private int[] _previous = [];
  private int _stride;
  private int _guard;

  public void BeginFrame(int widthCells, int heightCells)
  {
    _stride = widthCells + 1;
    _guard = _stride + 1;
    var cells = _stride * (heightCells + 2);
    if (_current.Length < cells)
    {
      _current = new int[cells];
      _previous = new int[cells];
    }
    (_current, _previous) = (_previous, _current);
  }

  public void Reset()
  {
    Array.Clear(_current);
    Array.Clear(_previous);
  }

  public int Index(int cellX, int cellY) => _guard + cellY * _stride + cellX;

  public void Store(int index, int mv) => _current[index] = mv;

  public void StoreSquare(int index, int cells, int mv)
  {
    for (var cy = 0; cy < cells; cy++, index += _stride)
    {
      for (var cx = 0; cx < cells; cx++)
        _current[index + cx] = mv;
    }
  }

  public bool NeighbourMoving(int index) =>
    _current[index - 1] != 0 || _current[index - _stride] != 0;

  public int Left(int index) => _current[index - 1];

  public int Above(int index) => _current[index - _stride];

  public int SpatialPredictor(int index) =>
    Median(_current[index - 1], _current[index - _stride], _current[index - _stride + 1]);

  public int SkipPredictor(int index)
  {
    var left = _current[index - 1];
    if (left == 0) return 0;
    var above = _current[index - _stride];
    if (above == 0) return 0;
    return Median(left, above, _current[index - _stride + 1]);
  }

  private static int Median(int a, int b, int c)
  {
    var x = MedianComponent((short)a, (short)b, (short)c);
    var y = MedianComponent(a >> 16, b >> 16, c >> 16);
    return (y << 16) | (x & 0xFFFF);
  }

  private static int MedianComponent(int a, int b, int c) =>
    Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

  public int Corroborated(int index) =>
    _current[index - 1] == 0 && _current[index - _stride] == 0 ? 0 : _previous[index];
}
