using static Utils.BitstreamHelpers;

namespace Analyzer.Thumbnail;

internal sealed record H264ScalingMatrix(byte[]?[] Lists4x4, byte[]?[] Lists8x8)
{
  private const int Flat = 16;
  private const int Lists = 6;

  private const int Kept8x8 = 2;

  public static H264ScalingMatrix Read(ReadOnlySpan<byte> data, ref int at, int count)
  {
    var lists4x4 = new byte[]?[Lists];
    var lists8x8 = new byte[]?[Kept8x8];

    for (var i = 0; i < count; i++)
    {
      if (!ReadBit(data, ref at)) continue;

      if (i < Lists)
      {
        lists4x4[i] = ReadList(
          data, ref at, H264.ResidualTables.Zigzag4x4, H264.ResidualTables.DefaultScaling4x4[i / 3]);
        continue;
      }

      var read = ReadList(
        data, ref at, H264.ResidualTables.Zigzag8x8,
        H264.ResidualTables.DefaultScaling8x8[(i - Lists) & 1]);

      if (i - Lists < Kept8x8)
        lists8x8[i - Lists] = read;
    }

    return new H264ScalingMatrix(lists4x4, lists8x8);
  }

  private static byte[] ReadList(
    ReadOnlySpan<byte> data, ref int at, ReadOnlySpan<byte> scan, byte[] fallback)
  {
    var list = new byte[scan.Length];
    var last = 8;
    var next = 8;

    for (var j = 0; j < scan.Length; j++)
    {
      if (next != 0)
      {
        next = (last + (int)ReadSignedExpGolomb(data, ref at) + 256) % 256;
        if (j == 0 && next == 0)
          return fallback;
      }

      list[scan[j]] = (byte)(next == 0 ? last : next);
      last = list[scan[j]];
    }

    return list;
  }

  public static (byte[][] Lists4x4, byte[][] Lists8x8) Resolve(
    H264ScalingMatrix? sequence, H264ScalingMatrix? picture)
  {
    if (sequence == null && picture == null)
      return (Uniform(Lists, 16), Uniform(Kept8x8, 64));

    var intra4x4 = H264.ResidualTables.DefaultScaling4x4[0];
    var inter4x4 = H264.ResidualTables.DefaultScaling4x4[1];
    var intra8x8 = H264.ResidualTables.DefaultScaling8x8[0];
    var inter8x8 = H264.ResidualTables.DefaultScaling8x8[1];

    if (sequence == null)
      return Fill(picture!, intra4x4, inter4x4, intra8x8, inter8x8);

    var resolved = Fill(sequence, intra4x4, inter4x4, intra8x8, inter8x8);
    if (picture == null)
      return resolved;

    return Fill(
      picture, resolved.Lists4x4[0], resolved.Lists4x4[3],
      resolved.Lists8x8[0], resolved.Lists8x8[1]);
  }

  private static (byte[][] Lists4x4, byte[][] Lists8x8) Fill(
    H264ScalingMatrix matrix,
    byte[] intra4x4, byte[] inter4x4, byte[] intra8x8, byte[] inter8x8)
  {
    var lists4x4 = new byte[Lists][];
    for (var i = 0; i < Lists; i++)
      lists4x4[i] = matrix.Lists4x4[i]
        ?? (i == 0 ? intra4x4 : i == 3 ? inter4x4 : lists4x4[i - 1]);

    byte[][] lists8x8 =
    [
      matrix.Lists8x8[0] ?? intra8x8,
      matrix.Lists8x8[1] ?? inter8x8,
    ];

    return (lists4x4, lists8x8);
  }

  private static byte[][] Uniform(int count, int size)
  {
    var lists = new byte[count][];
    for (var i = 0; i < count; i++)
    {
      lists[i] = new byte[size];
      Array.Fill(lists[i], (byte)Flat);
    }

    return lists;
  }
}

internal sealed class H264Dequant
{
  private const int QpRange = 52;

  public const int Shift = 4;

  public required int[][] Luma4x4 { get; init; }
  public required int[][] Cb4x4 { get; init; }
  public required int[][] Cr4x4 { get; init; }
  public required int[][] Luma8x8 { get; init; }

  public static H264Dequant Build(H264ScalingMatrix? sequence, H264ScalingMatrix? picture)
  {
    var (lists4x4, lists8x8) = H264ScalingMatrix.Resolve(sequence, picture);

    return new H264Dequant
    {
      Luma4x4 = Scale4x4(lists4x4[0]),
      Cb4x4 = Scale4x4(lists4x4[1]),
      Cr4x4 = Scale4x4(lists4x4[2]),
      Luma8x8 = Scale8x8(lists8x8[0]),
    };
  }

  private static int[][] Scale4x4(byte[] list)
  {
    var table = new int[QpRange][];
    for (var qp = 0; qp < QpRange; qp++)
    {
      var scale = H264.ResidualTables.DequantCoeff4x4[qp];
      var row = new int[16];
      for (var i = 0; i < 16; i++)
        row[i] = list[i] * scale[i & 7];

      table[qp] = row;
    }

    return table;
  }

  private static int[][] Scale8x8(byte[] list)
  {
    var table = new int[QpRange][];
    for (var qp = 0; qp < QpRange; qp++)
    {
      var scale = H264.ResidualTables.DequantCoeff8x8[qp];
      var row = new int[64];
      for (var i = 0; i < 64; i++)
        row[i] = list[i] * (scale[i] >> Shift);

      table[qp] = row;
    }

    return table;
  }
}
