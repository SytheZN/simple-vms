using System.Numerics;

namespace Analyzer.Thumbnail;

internal static class H265IntraPrediction
{
  private const int BitDepth = 8;
  private const int Neutral = 1 << (BitDepth - 1);

  internal readonly struct Neighbourhood
  {
    public required byte[] Band { get; init; }
    public required int BandWidth { get; init; }

    public required int BandTop { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }

    public required byte[] Decoded { get; init; }
    public required int DecodedStride { get; init; }
    public required int DecodedShift { get; init; }
  }

  internal readonly struct Workspace
  {
    public required byte[] References { get; init; }
    public required int[] Main { get; init; }
    public required int[] Sums { get; init; }

    public required byte[] Bottom { get; init; }
    public required byte[] Right { get; init; }

    public required byte[] Means { get; init; }

    public IObserverHarness<ReconstructionPhase>? Observer { get; init; }
  }

  public static void Reference(
    in Neighbourhood neighbourhood, in Workspace work, int x0, int y0, int size,
    byte[]? pairedBand, Span<byte> paired)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Gather);

    var found = pairedBand == null
      ? Gather(neighbourhood, work.References, x0, y0, size, 2 * size)
      : GatherPair(neighbourhood, work.References, pairedBand, paired, x0, y0, size, 2 * size);

    if (!found)
    {
      var count = 4 * size + 1;
      work.References.AsSpan(0, count).Fill(Neutral);
      if (pairedBand != null) paired[..count].Fill(Neutral);
    }

    observer?.End(ReconstructionPhase.Gather);
  }

  public static void Predict(
    in Workspace work, int size, int cells, int mode, bool isLuma, bool strongSmoothing)
  {
    var corner = 2 * size;
    var observer = work.Observer;

    observer?.Begin(ReconstructionPhase.Smooth);
    if (Filtered(mode, size, isLuma))
      Smooth(work, size, strongSmoothing && isLuma && size == 32);
    observer?.End(ReconstructionPhase.Smooth);

    observer?.Begin(ReconstructionPhase.Predict);

    if (mode == 1)
      Dc(work, size, cells, corner, isLuma);
    else if (mode == 0)
      Planar(work, size, cells, corner);
    else
      Angular(work, size, cells, mode, corner, isLuma);

    observer?.End(ReconstructionPhase.Predict);
  }

  private static bool Gather(
    in Neighbourhood neighbourhood, Span<byte> references, int x0, int y0, int size, int corner)
  {
    var extent = 2 * size;
    var shift = neighbourhood.DecodedShift;
    var stride = neighbourhood.DecodedStride;
    var decoded = neighbourhood.Decoded;
    var band = neighbourhood.Band;
    var bandWidth = neighbourhood.BandWidth;

    var previous = (byte)0;
    var first = -1;

    var column = x0 - 1;
    var columnEntry = column >> shift;

    if (column >= 0)
    {
      var py = Math.Min(y0 + extent - 1, neighbourhood.Height - 1);
      var at = (py - neighbourhood.BandTop) * bandWidth + column;

      for (var i = y0 + extent - 1 - py; i < extent;)
      {
        var entry = py >> shift;
        var run = Math.Min(py - (entry << shift) + 1, extent - i);

        if (decoded[entry * stride + columnEntry] != 0)
        {
          if (first < 0) first = i;

          for (var k = 0; k < run; k++, at -= bandWidth)
            references[i + k] = previous = band[at];
        }
        else
        {
          references.Slice(i, run).Fill(previous);
          at -= run * bandWidth;
        }

        i += run;
        py -= run;
      }
    }

    var above = y0 - 1;
    var aboveEntries = above >= 0 ? (above >> shift) * stride : 0;

    if (above >= 0 && column >= 0 && decoded[aboveEntries + columnEntry] != 0)
    {
      previous = band[(above - neighbourhood.BandTop) * bandWidth + column];
      if (first < 0) first = corner;
    }

    references[corner] = previous;

    if (above >= 0)
    {
      var row = (above - neighbourhood.BandTop) * bandWidth;
      var inside = Math.Min(extent, neighbourhood.Width - x0);

      for (var i = 0; i < inside;)
      {
        var px = x0 + i;
        var entry = px >> shift;
        var run = Math.Min(((entry + 1) << shift) - px, inside - i);
        var into = corner + 1 + i;

        if (decoded[aboveEntries + entry] != 0)
        {
          if (first < 0) first = into;

          for (var k = 0; k < run; k++)
            references[into + k] = previous = band[row + px + k];
        }
        else
          references.Slice(into, run).Fill(previous);

        i += run;
      }

      references.Slice(corner + 1 + inside, extent - inside).Fill(previous);
    }
    else
      references.Slice(corner + 1, extent).Fill(previous);

    if (first < 0) return false;
    if (first > 0) references[..first].Fill(references[first]);
    return true;
  }

  private static bool GatherPair(
    in Neighbourhood neighbourhood, Span<byte> references, byte[] secondBand, Span<byte> second,
    int x0, int y0, int size, int corner)
  {
    var extent = 2 * size;
    var shift = neighbourhood.DecodedShift;
    var stride = neighbourhood.DecodedStride;
    var decoded = neighbourhood.Decoded;
    var band = neighbourhood.Band;
    var bandWidth = neighbourhood.BandWidth;

    var previous = (byte)0;
    var second1 = (byte)0;
    var first = -1;

    var column = x0 - 1;
    var columnEntry = column >> shift;

    if (column >= 0)
    {
      var py = Math.Min(y0 + extent - 1, neighbourhood.Height - 1);
      var at = (py - neighbourhood.BandTop) * bandWidth + column;

      for (var i = y0 + extent - 1 - py; i < extent;)
      {
        var entry = py >> shift;
        var run = Math.Min(py - (entry << shift) + 1, extent - i);

        if (decoded[entry * stride + columnEntry] != 0)
        {
          if (first < 0) first = i;

          for (var k = 0; k < run; k++, at -= bandWidth)
          {
            references[i + k] = previous = band[at];
            second[i + k] = second1 = secondBand[at];
          }
        }
        else
        {
          references.Slice(i, run).Fill(previous);
          second.Slice(i, run).Fill(second1);
          at -= run * bandWidth;
        }

        i += run;
        py -= run;
      }
    }

    var above = y0 - 1;
    var aboveEntries = above >= 0 ? (above >> shift) * stride : 0;

    if (above >= 0 && column >= 0 && decoded[aboveEntries + columnEntry] != 0)
    {
      var at = (above - neighbourhood.BandTop) * bandWidth + column;
      previous = band[at];
      second1 = secondBand[at];
      if (first < 0) first = corner;
    }

    references[corner] = previous;
    second[corner] = second1;

    if (above >= 0)
    {
      var row = (above - neighbourhood.BandTop) * bandWidth;
      var inside = Math.Min(extent, neighbourhood.Width - x0);

      for (var i = 0; i < inside;)
      {
        var px = x0 + i;
        var entry = px >> shift;
        var run = Math.Min(((entry + 1) << shift) - px, inside - i);
        var into = corner + 1 + i;

        if (decoded[aboveEntries + entry] != 0)
        {
          if (first < 0) first = into;

          for (var k = 0; k < run; k++)
          {
            references[into + k] = previous = band[row + px + k];
            second[into + k] = second1 = secondBand[row + px + k];
          }
        }
        else
        {
          references.Slice(into, run).Fill(previous);
          second.Slice(into, run).Fill(second1);
        }

        i += run;
      }

      references.Slice(corner + 1 + inside, extent - inside).Fill(previous);
      second.Slice(corner + 1 + inside, extent - inside).Fill(second1);
    }
    else
    {
      references.Slice(corner + 1, extent).Fill(previous);
      second.Slice(corner + 1, extent).Fill(second1);
    }

    if (first < 0) return false;

    if (first > 0)
    {
      references[..first].Fill(references[first]);
      second[..first].Fill(second[first]);
    }

    return true;
  }

  private static bool Filtered(int mode, int size, bool isLuma)
  {
    if (!isLuma || mode == 1) return false;

    var distance = Math.Min(Math.Abs(mode - 26), Math.Abs(mode - 10));
    return distance > H265.ResidualTables.IntraFilterThreshold[Log2(size) - 2];
  }

  private static void Smooth(in Workspace work, int size, bool allowStrong)
  {
    Span<byte> references = work.References;
    var count = 4 * size + 1;
    var corner = 2 * size;
    var threshold = 1 << (BitDepth - 5);

    var start = references[0];
    var pivot = references[corner];
    var end = references[count - 1];

    if (allowStrong
        && Math.Abs(pivot + end - 2 * references[corner + size]) < threshold
        && Math.Abs(pivot + start - 2 * references[size]) < threshold)
    {
      for (var i = 1; i < 2 * size; i++)
      {
        references[corner + i] = (byte)(((64 - i) * pivot + i * end + 32) >> 6);
        references[corner - i] = (byte)(((64 - i) * pivot + i * start + 32) >> 6);
      }

      return;
    }

    var previous = (int)start;
    for (var i = 1; i < count - 1; i++)
    {
      var current = references[i];
      references[i] = (byte)((previous + 2 * current + references[i + 1] + 2) >> 2);
      previous = current;
    }
  }

  private static void Planar(in Workspace work, int size, int cells, int corner)
  {
    ReadOnlySpan<byte> references = work.References;
    Span<int> scratch = work.Main;
    Span<byte> bottom = work.Bottom;
    Span<byte> right = work.Right;
    Span<byte> means = work.Means;

    var shift = Log2(size) + 1;
    var topRight = references[corner + 1 + size];
    var bottomLeft = references[corner - 1 - size];
    var last = size - 1;

    var leftLast = references[corner - 1 - last];
    var along = last * leftLast + topRight + size * bottomLeft + size;
    var stepAlong = topRight - leftLast;

    for (var x = 0; x < size; x++, along += stepAlong)
      bottom[x] = (byte)(along >> shift);

    var aboveLast = references[corner + 1 + last];
    var down = size * topRight + last * aboveLast + bottomLeft + size;
    var stepDown = bottomLeft - aboveLast;

    for (var y = 0; y < size; y++, down += stepDown)
      right[y] = (byte)(down >> shift);

    var spanShift = Log2(size) - Log2(cells);
    var span = 1 << spanShift;
    var scaleShift = 2 * spanShift + 1 + Log2(size);
    var bias = span * span * size;
    var rise = span * (span - 1) / 2;
    var fall = span * (span + 1) / 2;

    if (cells == 1)
    {
      var edges = 0;
      for (var i = 0; i < size; i++)
        edges += references[corner + 1 + i] + references[corner - 1 - i];

      means[0] = (byte)(
        (edges * (span * last - rise) + fall * span * (topRight + bottomLeft) + bias) >> scaleShift);
      return;
    }

    var aboveSums = scratch[..cells];
    var leftWeights = scratch.Slice(cells, cells);
    var topRightWeights = scratch.Slice(2 * cells, cells);

    for (var c = 0; c < cells; c++)
    {
      var start = c * span;
      var total = 0;
      for (var i = 0; i < span; i++)
        total += references[corner + 1 + start + i];

      aboveSums[c] = total;
      leftWeights[c] = span * last - (span * start + rise);
      topRightWeights[c] = (span * start + fall) * span;
    }

    for (var cy = 0; cy < cells; cy++)
    {
      var start = cy * span;
      var leftSum = 0;
      for (var i = 0; i < span; i++)
        leftSum += references[corner - 1 - start - i];

      var aboveWeight = span * last - (span * start + rise);
      var bottomLeftTerm = (span * start + fall) * span * bottomLeft;

      var at = cy * cells;

      for (var cx = 0; cx < cells; cx++)
        means[at + cx] = (byte)(
          (leftSum * leftWeights[cx] + topRight * topRightWeights[cx]
           + aboveSums[cx] * aboveWeight + bottomLeftTerm + bias) >> scaleShift);
    }
  }

  private static void Dc(in Workspace work, int size, int cells, int corner, bool isLuma)
  {
    ReadOnlySpan<byte> references = work.References;
    Span<byte> bottom = work.Bottom;
    Span<byte> right = work.Right;
    Span<byte> means = work.Means;

    var total = size;
    for (var i = 0; i < size; i++)
      total += references[corner + 1 + i] + references[corner - 1 - i];

    var dc = (byte)(total >> (Log2(size) + 1));
    var filtered = isLuma && size < 32;
    var last = size - 1;

    for (var i = 0; i < size; i++)
    {
      bottom[i] = dc;
      right[i] = dc;
    }

    var count = cells * cells;
    for (var i = 0; i < count; i++)
      means[i] = dc;

    if (!filtered) return;

    bottom[0] = Row(references[corner - 1 - last], dc);
    right[0] = Row(references[corner + 1 + last], dc);

    var spanShift = Log2(size) - Log2(cells);
    var span = 1 << spanShift;
    var samplesShift = 2 * spanShift;
    var samples = span * span;
    var corrected = (references[corner - 1] + 2 * dc + references[corner + 1] + 2) >> 2;

    var mask = samples - 1;
    var at = 0;
    var down = 0;

    for (var c = 0; c < cells; c++, down += cells)
    {
      var acrossTop = 0;
      var downLeft = 0;

      for (var i = 0; i < span; i++, at++)
      {
        acrossTop += (at == 0 ? corrected : Row(references[corner + 1 + at], dc)) - dc;
        if (at > 0) downLeft += Row(references[corner - 1 - at], dc) - dc;
      }

      means[c] = (byte)Math.Clamp(
        dc + ((acrossTop + ((acrossTop >> 31) & mask)) >> samplesShift), 0, 255);
      means[down] = (byte)Math.Clamp(
        means[down] + ((downLeft + ((downLeft >> 31) & mask)) >> samplesShift), 0, 255);
    }
  }

  private static byte Row(int reference, byte dc) => (byte)((reference + 3 * dc + 2) >> 2);

  private static void Walk(
    in Workspace work, int size, int cells, int origin, int angle, bool vertical)
  {
    ReadOnlySpan<int> main = work.Main;
    Span<int> sums = work.Sums;
    Span<byte> bottom = work.Bottom;
    Span<byte> right = work.Right;
    Span<byte> means = work.Means;

    var spanShift = Log2(size) - Log2(cells);
    var span = 1 << spanShift;
    var samplesShift = 2 * spanShift;

    var acrossFar = vertical ? bottom : right;
    var downFar = vertical ? right : bottom;

    sums[..cells].Clear();

    for (var u = 0; u < size; u++)
    {
      var step = (u + 1) * angle;
      var at = origin + (step >> 5) + 1;
      var fraction = step & 31;
      var value = 0;

      for (var v = 0; v < size; v++)
      {
        value = ((32 - fraction) * main[at + v] + fraction * main[at + v + 1] + 16) >> 5;
        sums[v >> spanShift] += value;
      }

      downFar[u] = (byte)value;

      if ((u & (span - 1)) != span - 1) continue;

      var line = u >> spanShift;
      var cell = vertical ? line * cells : line;
      var stride = vertical ? 1 : cells;

      for (var c = 0; c < cells; c++, cell += stride)
      {
        means[cell] = (byte)(sums[c] >> samplesShift);
        sums[c] = 0;
      }
    }

    var edge = size * angle;
    var from = origin + (edge >> 5) + 1;
    var blend = edge & 31;

    for (var v = 0; v < size; v++)
      acrossFar[v] =
        (byte)(((32 - blend) * main[from + v] + blend * main[from + v + 1] + 16) >> 5);
  }

  private static void Angular(
    in Workspace work, int size, int cells, int mode, int corner, bool isLuma)
  {
    ReadOnlySpan<byte> references = work.References;
    Span<int> main = work.Main;

    var vertical = mode >= 18;
    var angleMode = vertical ? mode - 26 : 10 - mode;
    var absAngle = H265.ResidualTables.IntraPredAngle[Math.Abs(angleMode)];
    var angle = angleMode < 0 ? -absAngle : absAngle;

    var origin = 32;

    var step = vertical ? 1 : -1;

    for (var i = 0; i <= 2 * size; i++)
      main[origin + i] = references[corner + i * step];

    if (angle < 0)
    {
      var invAngle = H265.ResidualTables.IntraInvAngle[Math.Abs(angleMode)];
      var extent = (size * angle) >> 5;

      for (var i = -1; i > extent; i--)
        main[origin + i] = references[corner - ((-i * invAngle + 128) >> 8) * step];
    }

    if (!isLuma || size >= 32 || (mode != 10 && mode != 26))
    {
      Walk(work, size, cells, origin, angle, vertical);
      return;
    }

    Flat(work, size, cells, corner, vertical ? 1 : -1, vertical);
  }

  private static void Flat(
    in Workspace work, int size, int cells, int corner, int direction, bool vertical)
  {
    ReadOnlySpan<byte> references = work.References;
    Span<int> sums = work.Sums;
    Span<byte> bottom = work.Bottom;
    Span<byte> right = work.Right;
    Span<byte> means = work.Means;

    var pivot = references[corner];
    var lead = references[corner + direction];

    var spanShift = Log2(size) - Log2(cells);
    var span = 1 << spanShift;
    var samplesShift = 2 * spanShift;

    var filtered = sums[..cells];
    filtered.Clear();

    for (var i = 0; i < size; i++)
      filtered[i >> spanShift] +=
        Clip(lead + ((references[corner - direction * (1 + i)] - pivot) >> 1));

    var flatEdge = vertical ? right : bottom;
    var leadEdge = vertical ? bottom : right;

    flatEdge[..size].Fill(references[corner + direction * size]);

    leadEdge[0] = Clip(lead + ((references[corner - direction * size] - pivot) >> 1));
    for (var i = 1; i < size; i++)
      leadEdge[i] = references[corner + direction * (1 + i)];

    for (var line = 0; line < cells; line++)
    {
      var total = 0;
      for (var i = line == 0 ? 1 : 0; i < span; i++)
        total += references[corner + direction * (1 + (line << spanShift) + i)];
      total *= span;

      var at = vertical ? line : line * cells;
      var stride = vertical ? cells : 1;

      for (var c = 0; c < cells; c++, at += stride)
        means[at] = (byte)((total + (line == 0 ? filtered[c] : 0)) >> samplesShift);
    }
  }

  private static byte Clip(int value) => (byte)Math.Clamp(value, 0, 255);

  private static int Log2(int size) => BitOperations.Log2((uint)size);
}
