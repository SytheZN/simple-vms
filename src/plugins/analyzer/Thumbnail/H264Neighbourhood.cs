using H264;

namespace Analyzer.Thumbnail;

[Flags]
internal enum H264Neighbours
{
  None = 0,
  Left = 1,
  Top = 2,
  TopLeft = 4,
  TopRight = 8,
}

internal readonly struct H264Neighbourhood
{
  public required byte[] Band { get; init; }
  public required int BandWidth { get; init; }

  public required int BandTop { get; init; }
}

internal static class H264Availability
{
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

  public static readonly byte[] Blocks4x4 = Build(4, BlockOrder.Index);

  public static readonly byte[] Blocks8x8 = Build(2, [0, 1, 2, 3]);

  public static readonly byte[] Whole = Build(1, [0]);

  public static H264Neighbours Of(byte[] table, int block, int mask) =>
    (H264Neighbours)table[block * Combinations + mask];

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

internal readonly struct H264Workspace
{
  public required byte[] References { get; init; }

  public required byte[] Bottom { get; init; }
  public required byte[] Right { get; init; }

  public required byte[] Means { get; init; }

  public IObserverHarness<ReconstructionPhase>? Observer { get; init; }

  public static byte Combine(byte prediction, int residual) =>
    (byte)Math.Clamp(prediction + residual, 0, 255);

  public static int Above(int at) => 1 + at;

  public static int Left(int size, int at) => 1 + 2 * size + at;

  public const int Corner = 0;

  public static int Length(int size) => 3 * size + 1;
}
