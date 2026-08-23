namespace Analyzer.Thumbnail;

internal enum H265ScanIdx { Diagonal = 0, Horizontal = 1, Vertical = 2 }

/// <summary>
/// HM builds its scan tables at startup rather than storing them, so they are generated here too.
/// The same three patterns order positions inside a 4x4 group and groups inside a block, so scans
/// are cached per side length (1, 2, 4 and 8 groups across).
/// </summary>
internal static class H265ScanOrder
{
  private static readonly byte[][] Scans = BuildAll();
  private static readonly byte[][] Inverses = BuildInverses();

  /// <summary>
  /// Returns raster indices in scan order for a grid 1 shl <paramref name="log2Side"/> across.
  /// Scan and side fold into one index so a lookup is a single load rather than a walk down a
  /// jagged array, which the residual parser does four times per coded block.
  /// </summary>
  public static byte[] For(H265ScanIdx scanIdx, int log2Side) =>
    Scans[((int)scanIdx << 2) | log2Side];

  /// <summary>
  /// The same tables read the other way, giving a position's place in the scan. The last coded
  /// position arrives as coordinates and is needed as a scan index, which is otherwise a search.
  /// </summary>
  public static byte[] Inverse(H265ScanIdx scanIdx, int log2Side) =>
    Inverses[((int)scanIdx << 2) | log2Side];

  private static byte[][] BuildInverses()
  {
    var all = new byte[3 * 4][];
    for (var i = 0; i < all.Length; i++)
    {
      var forward = Scans[i];
      var inverse = new byte[forward.Length];
      for (var n = 0; n < forward.Length; n++)
        inverse[forward[n]] = (byte)n;
      all[i] = inverse;
    }
    return all;
  }

  private static byte[][] BuildAll()
  {
    var all = new byte[3 * 4][];
    for (var scanIdx = 0; scanIdx < 3; scanIdx++)
      for (var log2Side = 0; log2Side < 4; log2Side++)
        all[(scanIdx << 2) | log2Side] = Build((H265ScanIdx)scanIdx, 1 << log2Side);
    return all;
  }

  private static byte[] Build(H265ScanIdx scanIdx, int side) => scanIdx switch
  {
    H265ScanIdx.Horizontal => BuildHorizontal(side),
    H265ScanIdx.Vertical => BuildVertical(side),
    _ => BuildDiagonal(side)
  };

  private static byte[] BuildDiagonal(int side)
  {
    var order = new byte[side * side];
    var n = 0;
    for (var diagonal = 0; diagonal < side * 2 - 1; diagonal++)
      for (var y = Math.Min(diagonal, side - 1); y >= 0; y--)
      {
        var x = diagonal - y;
        if (x >= side) continue;
        order[n++] = (byte)(y * side + x);
      }
    return order;
  }

  private static byte[] BuildHorizontal(int side)
  {
    var order = new byte[side * side];
    for (var i = 0; i < order.Length; i++)
      order[i] = (byte)i;
    return order;
  }

  private static byte[] BuildVertical(int side)
  {
    var order = new byte[side * side];
    var n = 0;
    for (var x = 0; x < side; x++)
      for (var y = 0; y < side; y++)
        order[n++] = (byte)(y * side + x);
    return order;
  }

}
