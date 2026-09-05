namespace H265;

public static class ScanOrder
{
  private static readonly byte[][] Scans = BuildAll();
  private static readonly byte[][] Inverses = BuildInverses();

  public static byte[] For(ScanIdx scanIdx, int log2Side) =>
    Scans[((int)scanIdx << 2) | log2Side];

  public static byte[] Inverse(ScanIdx scanIdx, int log2Side) =>
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
        all[(scanIdx << 2) | log2Side] = Build((ScanIdx)scanIdx, 1 << log2Side);
    return all;
  }

  private static byte[] Build(ScanIdx scanIdx, int side) => scanIdx switch
  {
    ScanIdx.Horizontal => BuildHorizontal(side),
    ScanIdx.Vertical => BuildVertical(side),
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
