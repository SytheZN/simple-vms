namespace Analyzer.Thumbnail;

/// <summary>
/// Which neighbours a block actually has. H.264 has no substitution scan: a mode that would read a
/// missing neighbour is one the encoder was not allowed to signal, and the modes that survive on
/// partial neighbours have their own variants. So availability is an input to prediction rather
/// than something the gather resolves away.
/// </summary>
[Flags]
internal enum H264Neighbours
{
  None = 0,
  Left = 1,
  Top = 2,
  TopLeft = 4,
  TopRight = 8,
}

/// <summary>
/// Where reference samples are read from. Nothing in it changes within a macroblock row, so the
/// caller builds one per plane per row rather than one per block.
/// </summary>
internal readonly struct H264Neighbourhood
{
  public required byte[] Band { get; init; }
  public required int BandWidth { get; init; }

  /// <summary>Picture row held by the band's first row.</summary>
  public required int BandTop { get; init; }
}

/// <summary>
/// Which neighbours each block has. Blocks sit on a fixed grid in a fixed order, so this follows
/// from the block's index and the four neighbours the macroblock itself has - there is nothing to
/// record as the picture is decoded.
/// </summary>
internal static class H264Availability
{
  /// <summary>Bit positions of the four neighbours a macroblock reports, as <see cref="Mask"/> packs them.</summary>
  private const int MbLeft = 0;
  private const int MbAbove = 1;
  private const int MbAboveLeft = 2;
  private const int MbAboveRight = 3;

  public const int Combinations = 1 << 4;

  public static int Mask(bool left, bool above, bool aboveLeft, bool aboveRight) =>
    (left ? 1 << MbLeft : 0)
    | (above ? 1 << MbAbove : 0)
    | (aboveLeft ? 1 << MbAboveLeft : 0)
    | (aboveRight ? 1 << MbAboveRight : 0);

  /// <summary>The sixteen 4x4 blocks, in the Morton order they are coded in.</summary>
  public static readonly byte[] Blocks4x4 = Build(4, H264BlockOrder.Index);

  /// <summary>The four 8x8 blocks, which are coded in raster order.</summary>
  public static readonly byte[] Blocks8x8 = Build(2, [0, 1, 2, 3]);

  /// <summary>One block covering the macroblock, which is what Intra_16x16 and chroma predict as.</summary>
  public static readonly byte[] Whole = Build(1, [0]);

  public static H264Neighbours Of(byte[] table, int block, int mask) =>
    (H264Neighbours)table[block * Combinations + mask];

  /// <summary>
  /// <paramref name="order"/> turns a block's place in the grid into its place in the coding order,
  /// which is the only thing that decides whether the block above and to the right is already there.
  /// </summary>
  private static byte[] Build(int side, ReadOnlySpan<byte> order)
  {
    var table = new byte[side * side * Combinations];

    for (var by = 0; by < side; by++)
      for (var bx = 0; bx < side; bx++)
      {
        var block = order[by * side + bx];

        for (var mask = 0; mask < Combinations; mask++)
        {
          var found = H264Neighbours.None;

          if (bx > 0 || (mask & (1 << MbLeft)) != 0)
            found |= H264Neighbours.Left;

          if (by > 0 || (mask & (1 << MbAbove)) != 0)
            found |= H264Neighbours.Top;

          var corner = bx > 0
            ? by > 0 ? -1 : MbAbove
            : by > 0 ? MbLeft : MbAboveLeft;

          if (corner < 0 || (mask & (1 << corner)) != 0)
            found |= H264Neighbours.TopLeft;

          // Above and to the right is the only direction that can point at a block of this
          // macroblock which the coding order has not reached yet, and at the right edge it points
          // out of the macroblock entirely - into one that is only ever there on the top row.
          var ahead = by > 0
            ? bx + 1 < side && order[(by - 1) * side + bx + 1] < block
            : bx + 1 < side ? (mask & (1 << MbAbove)) != 0 : (mask & (1 << MbAboveRight)) != 0;

          if (ahead)
            found |= H264Neighbours.TopRight;

          table[block * Combinations + mask] = (byte)found;
        }
      }

    return table;
  }
}

/// <summary>
/// The buffers prediction works in, all owned by the caller and none of them carrying anything from
/// one block to the next.
///
/// <see cref="References"/> is laid out the way the predictors read it: entry 0 is the corner
/// p[-1,-1], then the row above left to right including the above-right extension, then the left
/// column top to bottom. That costs a longer array than folding the corner into the middle would,
/// and buys every mode reading p[x,-1] and p[-1,y] at the index its formula already names.
/// </summary>
internal readonly struct H264Workspace
{
  public required byte[] References { get; init; }

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

  public static byte Combine(byte prediction, int residual) =>
    (byte)Math.Clamp(prediction + residual, 0, 255);

  public static int Above(int at) => 1 + at;

  public static int Left(int size, int at) => 1 + 2 * size + at;

  public const int Corner = 0;

  /// <summary>Corner, the row above with its above-right extension, then the left column.</summary>
  public static int Length(int size) => 3 * size + 1;
}
