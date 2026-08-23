using System.Numerics;
using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

/// <summary>
/// Scaling and transformation of transform coefficients, for 8-bit 4:2:0
/// without scaling lists. The matrices are stored the way HM holds them, indexed by frequency
/// then position; the inverse transform reads them the other way round.
///
/// The interior of a block is never read back - intra prediction only ever reads the edges of its
/// neighbours - so only the bottom row, the right column, and the block's average over each output
/// sample are produced. Taking a single row is two passes over the coefficients rather than the
/// three dimensions a whole block costs.
/// </summary>
internal static class H265InverseTransform
{
  private const int BitDepth = 8;
  private const int CoeffMin = -32768;
  private const int CoeffMax = 32767;
  private const int Stage1Shift = 7;
  private const int Stage2Shift = 20 - BitDepth;
  private const int MaxLog2Size = 5;

  private static readonly int[] LevelScale = [40, 45, 51, 57, 64, 72];

  /// <summary>
  /// Dequantisation with a flat scaling list. A QP names a step within an octave and the octave it
  /// sits in, and neither depends on the coefficient - so both fold into the single factor every
  /// level in the block is multiplied by. Quantisation parameters run 0 to 51.
  /// </summary>
  private static readonly int[] Scales = BuildScales();

  private static int[] BuildScales()
  {
    var scales = new int[52];
    for (var qp = 0; qp < scales.Length; qp++)
      scales[qp] = (16 * LevelScale[qp % 6]) << (qp / 6);
    return scales;
  }

  /// <summary>
  /// The full-size matrices with each basis averaged over the samples one output covers, indexed by
  /// coded size then output size. Transforming the low-frequency corner with the matrix for the
  /// smaller size is not the same thing: those are different basis functions, and the mismatch
  /// runs to a quarter of the amplitude on the highest frequency kept. Averaging the real basis
  /// makes the result the average of the full-size one by construction, and the frequencies dropped
  /// average to zero, so nothing is lost by discarding them.
  /// </summary>
  private static readonly short[][][] ReducedBasis = BuildReducedBases();

  private static short[][][] BuildReducedBases()
  {
    var bases = new short[MaxLog2Size + 1][][];

    for (var log2Size = 2; log2Size <= MaxLog2Size; log2Size++)
    {
      var source = DctFor(log2Size);
      var size = 1 << log2Size;
      bases[log2Size] = new short[log2Size + 1][];

      for (var log2Out = 0; log2Out <= log2Size; log2Out++)
      {
        var reduced = 1 << log2Out;
        var group = size / reduced;
        var basis = new short[reduced * reduced];

        for (var k = 0; k < reduced; k++)
          for (var j = 0; j < reduced; j++)
          {
            var total = 0;
            for (var i = 0; i < group; i++)
              total += source[k * size + j * group + i];
            basis[k * reduced + j] = (short)((total + (total < 0 ? -group / 2 : group / 2)) / group);
          }

        bases[log2Size][log2Out] = basis;
      }
    }

    return bases;
  }

  /// <summary>
  /// The buffers the transform works in, all owned by the caller. They travel as one reference
  /// rather than as arguments because every path threads most of them through two levels of call,
  /// and for a block holding a handful of levels that shuffling outweighs the arithmetic.
  /// <see cref="Block"/> and <see cref="Stage"/> are both left zeroed, so the next block never pays
  /// to clear what it does not write.
  /// </summary>
  internal readonly struct Workspace
  {
    public required int[] Block { get; init; }
    public required int[] Stage { get; init; }
    public required int[] EdgeStage { get; init; }

    /// <summary>The block's last row and last column, at full resolution.</summary>
    public required int[] Bottom { get; init; }
    public required int[] Right { get; init; }

    /// <summary>The block's average over each output sample.</summary>
    public required int[] Cells { get; init; }

    /// <summary>Null in production. The passes are too short to separate from outside.</summary>
    public IReconstructionObserver? Observer { get; init; }
  }

  /// <summary>
  /// Turns decoded levels into the parts of the residual that are read back: the block's last row
  /// and last column at full resolution, and its average over each output sample.
  /// <paramref name="occupied"/> lists where the parser put levels, which is what makes every pass
  /// here proportional to how many there are rather than to the block.
  /// </summary>
  public static void Apply(
    in Workspace work, int log2Size, int log2Out, int qp, bool transformSkip, bool dstVii,
    ReadOnlySpan<ushort> occupied, Span<int> levels)
  {
    var size = 1 << log2Size;
    var observer = work.Observer;

    if (log2Size > 2 && !transformSkip)
    {
      observer?.Begin(ReconstructionPhase.Edge);
      var columns = Accumulate(work, occupied, levels, size, log2Size, log2Out,
        DctFor(log2Size), ReducedBasis[log2Size][log2Out], qp);
      observer?.End(ReconstructionPhase.Edge);

      observer?.Begin(ReconstructionPhase.Cells);
      Cells(work, log2Out, ReducedBasis[log2Size][log2Out], columns);
      observer?.End(ReconstructionPhase.Cells);
      return;
    }

    observer?.Begin(ReconstructionPhase.Samples);

    // The skip path scales the coefficients straight into the residual domain, so they are already
    // samples. A 4x4 is the only size DST-VII appears at.
    var matrix = dstVii ? H265ResidualTables.Dst4 : H265ResidualTables.Dct4;

    if (transformSkip)
    {
      Dequantize(levels, log2Size, qp);
      Span<int> block = work.Block;
      Spread(work, occupied, levels, size);
      for (var i = 0; i < size * size; i++)
        block[i] <<= Stage1Shift;
      Rescale(block, size * size);
      Split(work, size, 1 << log2Out);
    }
    else if (log2Out == 0)
    {
      SmallEdgesAndMean(work, occupied, levels, matrix,
        dstVii ? Dst4RowSums : Dct4RowSums, log2Size, qp);
    }
    else
    {
      Small(work, occupied, levels, matrix, log2Size, qp);
      Rescale(work.Block, size * size);
      Split(work, size, 1 << log2Out);
    }

    observer?.End(ReconstructionPhase.Samples);
  }

  /// <summary>Lays the levels back out densely, which only the sample-domain paths need.</summary>
  public static void Spread(
    in Workspace work, ReadOnlySpan<ushort> occupied, ReadOnlySpan<int> levels, int size)
  {
    Span<int> block = work.Block;
    block[..(size * size)].Clear();
    for (var i = 0; i < occupied.Length; i++)
      block[occupied[i]] = levels[i];
  }

  /// <summary>Residual already in the sample domain, as transquant bypass leaves it.</summary>
  public static void Split(in Workspace work, int size, int reduced)
  {
    ReadOnlySpan<int> block = work.Block;
    Span<int> bottomRow = work.Bottom;
    Span<int> rightColumn = work.Right;
    Span<int> cells = work.Cells;

    block.Slice((size - 1) * size, size).CopyTo(bottomRow);
    for (var i = 0; i < size; i++)
      rightColumn[i] = block[i * size + size - 1];

    var factorShift = BitOperations.Log2((uint)size) - BitOperations.Log2((uint)reduced);
    var factor = 1 << factorShift;
    var samplesShift = 2 * factorShift;
    var mask = (1 << samplesShift) - 1;

    for (var y = 0; y < reduced; y++)
      for (var x = 0; x < reduced; x++)
      {
        var total = 0;
        for (var sy = 0; sy < factor; sy++)
        {
          var row = (y * factor + sy) * size + x * factor;
          for (var sx = 0; sx < factor; sx++)
            total += block[row + sx];
        }

        // Residual runs either way, so a negative is nudged by what the shift discards, which is
        // what keeps it rounding towards zero the way the division did.
        cells[y * reduced + x] = (total + ((total >> 31) & mask)) >> samplesShift;
      }
  }

  /// <summary>
  /// The one pass that reaches every level, feeding all three of the block's outputs from it: the
  /// last row, the last column, and the first stage of the reduced interior. Each is a sum over the
  /// coefficients, so each is an accumulator this walk adds into - splitting them into a pass apiece
  /// would mean reading every level, scaling it and taking its position apart two or three times
  /// over, which for a block holding a handful of them is most of the work.
  ///
  /// Fixing the row first collapses the edges' first pass to a single vector, so an edge costs two
  /// passes over the coefficients rather than a pass per row. The column takes the passes in the
  /// opposite order, which is the same arithmetic apart from where the intermediate rounding lands.
  ///
  /// Returns which columns of the reduced stage were reached, which is what the cells resume from.
  /// </summary>
  private static uint Accumulate(
    in Workspace work, ReadOnlySpan<ushort> occupied, ReadOnlySpan<int> levels, int size,
    int log2Size, int log2Out, short[] matrix, short[] averaged, int qp)
  {
    Span<int> stage = work.EdgeStage;
    Span<int> cellStage = work.Stage;

    var last = size - 1;
    var mask = size - 1;
    var reduced = 1 << log2Out;

    var stageBottom = stage[..32];
    var stageRight = stage.Slice(32, 32);
    var order = stage.Slice(64, 32);
    var scaled = stage.Slice(96, 32);

    var shift = BitDepth + log2Size - 5;
    var scale = Scales[qp];
    var offset = 1 << (shift - 1);

    uint columns = 0;
    uint rows = 0;
    uint cellColumns = 0;

    for (var i = 0; i < occupied.Length; i++)
    {
      var at = occupied[i];
      var level = Dequantized(levels[i], scale, offset, shift);
      var k = at >> log2Size;
      var l = at & mask;

      stageBottom[l] += matrix[k * size + last] * level;
      stageRight[k] += matrix[l * size + last] * level;
      columns |= 1u << l;
      rows |= 1u << k;

      // Only the low-frequency corner reaches the interior: a level outside it belongs to a
      // frequency whose average over an output sample is zero.
      if (k < reduced && l < reduced)
      {
        var basis = k * reduced;

        for (var y = 0; y < reduced; y++)
          cellStage[y * reduced + l] += averaged[basis + y] * level;

        cellColumns |= 1u << l;
      }
    }

    Project(stageBottom, columns, order, scaled, matrix, size, work.Bottom);
    Project(stageRight, rows, order, scaled, matrix, size, work.Right);
    return cellColumns;
  }

  /// <summary>
  /// What each basis row contributes to the sum of the samples it spreads across. Only the first is
  /// non-zero for the DCT, since every other row is as far above the mean as below it; DST-VII has
  /// no such symmetry, so both are tabulated and read the same way.
  /// </summary>
  private static readonly int[] Dct4RowSums = RowSums(H265ResidualTables.Dct4);
  private static readonly int[] Dst4RowSums = RowSums(H265ResidualTables.Dst4);

  private static int[] RowSums(short[] matrix)
  {
    var sums = new int[4];
    for (var row = 0; row < 4; row++)
      for (var x = 0; x < 4; x++)
        sums[row] += matrix[row * 4 + x];
    return sums;
  }

  /// <summary>
  /// A 4x4 whose whole area is one output sample. The edges are still taken position by position,
  /// since a later block predicts from them and any drift there compounds - but the sample the block
  /// averages to is the second stage summed rather than performed, which each basis row already
  /// knows how to answer. Nothing between the edges is ever formed, and the rounding that would have
  /// landed on sixteen samples lands once on the average of them.
  /// </summary>
  private static void SmallEdgesAndMean(
    in Workspace work, ReadOnlySpan<ushort> occupied, ReadOnlySpan<int> levels,
    short[] matrix, int[] rowSums, int log2Size, int qp)
  {
    Span<int> stage = work.Stage;
    Span<int> bottom = work.Bottom;
    Span<int> right = work.Right;

    var shift = BitDepth + log2Size - 5;
    var scale = Scales[qp];
    var offset = 1 << (shift - 1);

    uint columns = 0;

    for (var i = 0; i < occupied.Length; i++)
    {
      var at = occupied[i];
      var level = Dequantized(levels[i], scale, offset, shift);
      var basis = (at >> 2) * 4;
      var l = at & 3;

      for (var y = 0; y < 4; y++)
        stage[y * 4 + l] += matrix[basis + y] * level;

      columns |= 1u << l;
    }

    bottom[..4].Clear();
    right[..4].Clear();
    var total = 0;

    for (var m = columns; m != 0; m &= m - 1)
    {
      var l = BitOperations.TrailingZeroCount(m);
      var basis = l * 4;

      var s0 = Scale(stage, l);
      var s1 = Scale(stage, 4 + l);
      var s2 = Scale(stage, 8 + l);
      var s3 = Scale(stage, 12 + l);

      total += (s0 + s1 + s2 + s3) * rowSums[l];

      var far = matrix[basis + 3];
      right[0] += s0 * far;
      right[1] += s1 * far;
      right[2] += s2 * far;
      right[3] += s3 * far;

      for (var x = 0; x < 4; x++)
        bottom[x] += s3 * matrix[basis + x];
    }

    for (var i = 0; i < 4; i++)
    {
      bottom[i] = (bottom[i] + (1 << (Stage2Shift - 1))) >> Stage2Shift;
      right[i] = (right[i] + (1 << (Stage2Shift - 1))) >> Stage2Shift;
    }

    // The sixteen rounding terms the samples would each have carried, collected.
    var summed = (total + (1 << (Stage2Shift + 3))) >> Stage2Shift;
    work.Cells[0] = (summed + ((summed >> 31) & 15)) >> 4;
  }

  /// <summary>Takes the first stage's accumulator down to a coefficient, clearing it behind.</summary>
  private static int Scale(Span<int> stage, int at)
  {
    var level = Math.Clamp(
      (stage[at] + (1 << (Stage1Shift - 1))) >> Stage1Shift, CoeffMin, CoeffMax);
    stage[at] = 0;
    return level;
  }

  /// <summary>
  /// A whole 4x4, which is wanted in full because the smallest transform is smaller than one output
  /// sample. Each level touches one column of the first stage and one basis row of the second, so
  /// the two dense passes reduce to a walk over the levels and a walk over the columns they reached.
  /// </summary>
  private static void Small(
    in Workspace work, ReadOnlySpan<ushort> occupied, ReadOnlySpan<int> levels, short[] matrix,
    int log2Size, int qp)
  {
    Span<int> block = work.Block;
    Span<int> stage = work.Stage;

    var shift = BitDepth + log2Size - 5;
    var scale = Scales[qp];
    var offset = 1 << (shift - 1);

    uint columns = 0;

    for (var i = 0; i < occupied.Length; i++)
    {
      var at = occupied[i];
      var level = Dequantized(levels[i], scale, offset, shift);
      var basis = (at >> 2) * 4;
      var l = at & 3;

      for (var y = 0; y < 4; y++)
        stage[y * 4 + l] += matrix[basis + y] * level;

      columns |= 1u << l;
    }

    block[..16].Clear();

    for (var m = columns; m != 0; m &= m - 1)
    {
      var l = BitOperations.TrailingZeroCount(m);
      var basis = l * 4;

      for (var y = 0; y < 4; y++)
      {
        var level = Scale(stage, y * 4 + l);
        if (level == 0) continue;

        for (var x = 0; x < 4; x++)
          block[y * 4 + x] += level * matrix[basis + x];
      }
    }
  }

  /// <summary>
  /// The second pass of an edge, over only the positions the first pass reached. Compacting them
  /// first turns the inner loop into a contiguous run along one basis row, and clears the stage
  /// behind itself so the next block finds it zeroed.
  /// </summary>
  private static void Project(
    Span<int> stage, uint touched, Span<int> order, Span<int> scaled, short[] matrix, int size,
    Span<int> edge)
  {
    var used = 0;
    for (var m = touched; m != 0; m &= m - 1)
    {
      var i = BitOperations.TrailingZeroCount(m);
      order[used] = i * size;
      scaled[used] = Scale(stage, i);
      used++;
    }

    if (used == 0)
    {
      edge[..size].Clear();
      return;
    }

    const int round = 1 << (Stage2Shift - 1);
    var lastLevel = scaled[used - 1];
    var lastRow = order[used - 1];

    // The first basis row lands rather than accumulates, which is the same thing onto zeros without
    // the pass that puts them there; the last one carries the scaling down, which is the same thing
    // as a pass that only rescales, without the pass.
    if (used == 1)
    {
      for (var j = 0; j < size; j++)
        edge[j] = (lastLevel * matrix[lastRow + j] + round) >> Stage2Shift;
      return;
    }

    for (var j = 0; j < size; j++)
      edge[j] = scaled[0] * matrix[order[0] + j];

    for (var t = 1; t < used - 1; t++)
    {
      var level = scaled[t];
      var row = order[t];
      for (var j = 0; j < size; j++)
        edge[j] += level * matrix[row + j];
    }

    for (var j = 0; j < size; j++)
      edge[j] = (edge[j] + lastLevel * matrix[lastRow + j] + round) >> Stage2Shift;
  }

  /// <summary>
  /// The second stage of the block's average over each output sample, resuming from the stage the
  /// accumulating pass left and the columns it reached.
  /// </summary>
  private static void Cells(in Workspace work, int log2Out, short[] matrix, uint columns)
  {
    Span<int> stage = work.Stage;
    Span<int> cells = work.Cells;

    var reduced = 1 << log2Out;
    var count = reduced * reduced;

    cells[..count].Clear();

    for (var m = columns; m != 0; m &= m - 1)
    {
      var l = BitOperations.TrailingZeroCount(m);
      var basis = l * reduced;

      // The second pass reaches every position the first one wrote, so clearing behind it leaves
      // the stage zeroed for the next block without a pass that says so.
      for (var y = 0; y < reduced; y++)
      {
        var level = Scale(stage, y * reduced + l);
        if (level == 0) continue;

        for (var x = 0; x < reduced; x++)
          cells[y * reduced + x] += level * matrix[basis + x];
      }
    }

    Rescale(cells, count);
  }

  /// <summary>The second stage leaves the samples 20 - BitDepth bits above the sample domain.</summary>
  private static void Rescale(Span<int> block, int count)
  {
    for (var i = 0; i < count; i++)
      block[i] = (block[i] + (1 << (Stage2Shift - 1))) >> Stage2Shift;
  }

  private static short[] DctFor(int log2Size) => log2Size switch
  {
    2 => H265ResidualTables.Dct4,
    3 => H265ResidualTables.Dct8,
    4 => H265ResidualTables.Dct16,
    _ => H265ResidualTables.Dct32,
  };

  /// <summary>A flat scaling list, so every position shares one factor.</summary>
  private static int Dequantized(int level, int scale, int offset, int shift) =>
    (int)Math.Clamp(((long)level * scale + offset) >> shift, CoeffMin, CoeffMax);

  /// <summary>
  /// The sample-domain paths have no pass over the levels to fold the scaling into, so they take it
  /// as one.
  /// </summary>
  private static void Dequantize(Span<int> levels, int log2Size, int qp)
  {
    var shift = BitDepth + log2Size - 5;
    var scale = Scales[qp];
    var offset = 1 << (shift - 1);

    for (var i = 0; i < levels.Length; i++)
      levels[i] = Dequantized(levels[i], scale, offset, shift);
  }

}
