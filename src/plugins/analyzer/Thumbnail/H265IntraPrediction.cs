using System.Numerics;
using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

/// <summary>
/// Intra sample prediction for 8-bit 4:2:0.
///
/// Reference samples are held in one array running from the far end of the left column, through
/// the corner, to the far end of the row above, which is the order substitution fills gaps in.
///
/// References are read from a band of the picture and the prediction is written to a block-sized
/// buffer, because only the block's own edges are ever read back. Both are full resolution: the
/// prediction has to match what the encoder predicted or its residual corrects the wrong thing.
/// </summary>
internal static class H265IntraPrediction
{
  private const int BitDepth = 8;
  private const int Neutral = 1 << (BitDepth - 1);

  /// <summary>
  /// Where reference samples are read from, and which of them exist yet. Nothing in it changes
  /// within a coding tree row, so the caller builds one per plane per row rather than one per block.
  /// </summary>
  internal readonly struct Neighbourhood
  {
    public required byte[] Band { get; init; }
    public required int BandWidth { get; init; }

    /// <summary>Picture row held by the band's first row.</summary>
    public required int BandTop { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }

    public required byte[] Decoded { get; init; }
    public required int DecodedStride { get; init; }
    public required int DecodedShift { get; init; }
  }

  /// <summary>
  /// The buffers prediction works in, all owned by the caller and none of them carrying anything
  /// from one block to the next. They travel as one reference rather than as arguments because
  /// every mode threads most of them through two or three calls, and a block is small enough that
  /// shuffling them costs more than the prediction does.
  ///
  /// <see cref="Main"/> doubles as scratch. Sizing it per block would mean zeroing enough for a
  /// 32x32 one every time, and blocks are mostly 4x4 - the zeroing would cost more than the work.
  /// </summary>
  internal readonly struct Workspace
  {
    public required byte[] References { get; init; }
    public required int[] Main { get; init; }
    public required int[] Sums { get; init; }

    /// <summary>The two edges a later block predicts from.</summary>
    public required byte[] Bottom { get; init; }
    public required byte[] Right { get; init; }

    /// <summary>The block's average over each output sample.</summary>
    public required byte[] Means { get; init; }

    /// <summary>
    /// Null in production. Prediction's steps are too small and too interleaved to separate from
    /// outside, and the workspace is the one thing already reaching all of them.
    /// </summary>
    public IReconstructionObserver? Observer { get; init; }
  }

  /// <summary>
  /// Fills the reference array a later prediction reads, and optionally a second plane's alongside
  /// it. Two planes qualify when they share a geometry and a decoded map: they then ask the same
  /// availability questions of the same entries and get the same answers, so only the samples they
  /// copy afterwards differ. The chroma pair always qualifies.
  /// </summary>
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

  /// <summary>
  /// Produces the two edges a later block predicts from and the block's average over each output
  /// sample, from references the caller has already gathered. No mode forms the block itself.
  /// </summary>
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

  /// <summary>
  /// Fills the reference array from the band and substitutes for what is not decoded yet, reporting
  /// whether anything at all was available.
  ///
  /// The array is built in substitution order, so a gap can take the preceding value as it is
  /// written and only a gap before the first available sample needs a second look. Availability is
  /// tracked per decoded-map entry rather than per sample, since one entry covers a run of them, and
  /// the band address advances by one along the row above and by a stride down the left column.
  /// </summary>
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

    // Positions past the bottom of the picture cannot have been decoded, and neither can any of the
    // left column when the block is against the left edge. Those entries are left as they lie: they
    // precede the first available sample, which the substitution at the end overwrites them from.
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

      // One decoded-map entry answers for a run of samples, so the run is the unit: whether it is
      // there is asked once, and the samples then either come across together or take the value in
      // hand together.
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

  /// <summary>
  /// The same walk for two planes that share a geometry and a decoded map. Every question it asks -
  /// which entry answers for this run, how long the run is, whether it is decoded yet - has one
  /// answer for both, so it is asked once. Only the samples that come back differ, and the value a
  /// gap is filled from: the planes agree on where the gaps are, never on what fills them.
  ///
  /// Spelled out rather than folded into <see cref="Gather"/> with a flag. The one plane that never
  /// pairs is luma, and it walks half the blocks in the picture - a test per run it can never take
  /// costs it more than the call this saves.
  /// </summary>
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

  /// <summary>How far the mode is from horizontal or vertical decides the filter.</summary>
  private static bool Filtered(int mode, int size, bool isLuma)
  {
    if (!isLuma || mode == 1) return false;

    var distance = Math.Min(Math.Abs(mode - 26), Math.Abs(mode - 10));
    return distance > H265ResidualTables.IntraFilterThreshold[Log2(size) - 2];
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

    // Both filters keep the two ends and the corner, and neither reads a position after it has been
    // written, so they run in place: the ramp reads only those three, and the [1 2 1] window needs
    // just the one sample it is about to overwrite held back.
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

  /// <summary>
  /// The planar predictor is bilinear in the two reference edges and the two far corners, so
  /// none of what is kept needs the block formed. The last row is where the above term's weight
  /// reaches zero and the last column is where the left term's does, and a cell's mean separates
  /// into a sum over its references times a sum over its weights, both of which are runs.
  /// The workspace's main buffer holds the per-column sums and weights.
  /// </summary>
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

    // Along an edge the blend moves by a fixed step, since one weight rises as the other falls by
    // the same amount. So each position is the one before it plus that step.
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

    // A block covering one output sample has one weight per edge rather than a run of them, so the
    // sums it needs are the whole edges and nothing has to be laid out to hold them.
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

      // Every term is a sum of non-negative references times non-negative weights, and the divisor
      // is a power of two, so the division is the shift it would compile to only if it knew that.
      var at = cy * cells;

      for (var cx = 0; cx < cells; cx++)
        means[at + cx] = (byte)(
          (leftSum * leftWeights[cx] + topRight * topRightWeights[cx]
           + aboveSums[cx] * aboveWeight + bottomLeftTerm + bias) >> scaleShift);
    }
  }

  /// <summary>
  /// The DC block is one value everywhere except the edge filter's first row and column, so
  /// the edges and the averages follow without forming it. Only cells along the block's top or left
  /// differ from the flat value, and only by the samples the filter touched.
  /// </summary>
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

    // Runtime lengths between one and a few dozen, which a span fill reaches through a call it
    // cannot size at compile time - and the block that covers a single output sample, which is most
    // of them, fills exactly one byte.
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

      // These sums run either way, and a shift rounds a negative one further from zero than the
      // division did - so a negative is nudged first by everything the shift is about to discard.
      means[c] = (byte)Math.Clamp(
        dc + ((acrossTop + ((acrossTop >> 31) & mask)) >> samplesShift), 0, 255);
      means[down] = (byte)Math.Clamp(
        means[down] + ((downLeft + ((downLeft >> 31) & mask)) >> samplesShift), 0, 255);
    }
  }

  private static byte Row(int reference, byte dc) => (byte)((reference + 3 * dc + 2) >> 2);

  /// <summary>
  /// Interpolates the block without forming it. Each position is wanted by at most one edge and by
  /// exactly one cell, so the two edges are captured as they are passed and the cells accumulate a
  /// running sum, which is flushed every time a row of them is complete.
  ///
  /// The walk runs in the mode's own frame: <paramref name="vertical"/> modes step down rows and
  /// horizontal ones step across columns, and the block one produces is the transpose of the other.
  /// So the far edge in that frame is the bottom row one way round and the right column the other,
  /// and the cell sums are indexed transposed rather than walked differently.
  /// </summary>
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

      // A whole-sample step is the same blend with all the weight on the near sample: references
      // are never negative, so (32a + 16) >> 5 is a, and the two cases are one loop.
      for (var v = 0; v < size; v++)
      {
        value = ((32 - fraction) * main[at + v] + fraction * main[at + v + 1] + 16) >> 5;
        sums[v >> spanShift] += value;
      }

      downFar[u] = (byte)value;

      if ((u & (span - 1)) != span - 1) continue;

      // A horizontal mode walks the transpose of what a vertical one walks, so the cells it fills
      // are the same run read down instead of across.
      var line = u >> spanShift;
      var cell = vertical ? line * cells : line;
      var stride = vertical ? 1 : cells;

      for (var c = 0; c < cells; c++, cell += stride)
      {
        means[cell] = (byte)(sums[c] >> samplesShift);
        sums[c] = 0;
      }
    }

    // The far edge is one row of the walk out of size, so repeating it costs less than asking every
    // position of every row whether it is the one that gets kept.
    var edge = size * angle;
    var from = origin + (edge >> 5) + 1;
    var blend = edge & 31;

    for (var v = 0; v < size; v++)
      acrossFar[v] =
        (byte)(((32 - blend) * main[from + v] + blend * main[from + v + 1] + 16) >> 5);
  }

  /// <summary>
  /// An angular mode picks which edge leads; the other is projected onto it so a single walk
  /// covers both, which is why the two branches differ only in how samples are indexed.
  /// </summary>
  private static void Angular(
    in Workspace work, int size, int cells, int mode, int corner, bool isLuma)
  {
    ReadOnlySpan<byte> references = work.References;
    Span<int> main = work.Main;

    var vertical = mode >= 18;
    var angleMode = vertical ? mode - 26 : 10 - mode;
    var absAngle = H265ResidualTables.IntraPredAngle[Math.Abs(angleMode)];
    var angle = angleMode < 0 ? -absAngle : absAngle;

    var origin = 32;

    // Which edge leads is settled for the block, so it picks the walk rather than being asked at
    // every position of it.
    var step = vertical ? 1 : -1;

    for (var i = 0; i <= 2 * size; i++)
      main[origin + i] = references[corner + i * step];

    if (angle < 0)
    {
      // invAngle is negative where it is defined; HM stores the magnitude, so the projection
      // negates it.
      var invAngle = H265ResidualTables.IntraInvAngle[Math.Abs(angleMode)];
      // Exclusive: the last projected position is never read back, and projecting it would reach
      // past the reference samples that exist for a small block.
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

  /// <summary>
  /// The two modes whose angle is zero, where every line of the block is one reference sample
  /// repeated and only the leading line is filtered. Nothing varies along a line, so a cell's sum is
  /// its references times the length of the line rather than a pass over its samples, and the block
  /// the general path would have formed is never touched.
  ///
  /// <paramref name="direction"/> is which way the leading edge runs from the corner, which is all
  /// that separates the horizontal mode from the vertical one - the two blocks are transposes, so
  /// only where a cell lands differs.
  /// </summary>
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

    // The leading line is the only one that varies along itself, so it is summed per cell here and
    // charged to the cells the block's first line falls in.
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
