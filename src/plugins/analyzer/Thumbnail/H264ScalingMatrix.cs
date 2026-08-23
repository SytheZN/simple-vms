using Shared.Models.Formats;
using static Shared.Models.Formats.BitstreamHelpers;

namespace Analyzer.Thumbnail;

/// <summary>
/// The scaling matrices a parameter set signals. A list it leaves out stays null rather than being
/// filled in here: what an absent list falls back to depends on the other parameter set, and the
/// picture parameter set has no access to the sequence's while it is being read.
/// </summary>
internal sealed record H264ScalingMatrix(byte[]?[] Lists4x4, byte[]?[] Lists8x8)
{
  private const int Flat = 16;
  private const int Lists = 6;

  /// <summary>
  /// 4:4:4 signals six 8x8 lists rather than two. Only the first two are ever read back, but all of
  /// them have to be walked or the syntax after the matrix is misread.
  /// </summary>
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
          data, ref at, H264ResidualTables.Zigzag4x4, H264ResidualTables.DefaultScaling4x4[i / 3]);
        continue;
      }

      var read = ReadList(
        data, ref at, H264ResidualTables.Zigzag8x8,
        H264ResidualTables.DefaultScaling8x8[(i - Lists) & 1]);

      if (i - Lists < Kept8x8)
        lists8x8[i - Lists] = read;
    }

    return new H264ScalingMatrix(lists4x4, lists8x8);
  }

  /// <summary>
  /// A zero delta on the first entry means the list is not coded at all and the default matrix
  /// stands in its place - and the deltas that would have followed are not present either, so the
  /// walk has to stop rather than read past them.
  /// </summary>
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

  /// <summary>
  /// Fills in every list neither parameter set signalled. Within a matrix an absent list repeats
  /// the one before it, except the two that start a chain - the first intra and the first inter -
  /// which fall back one level out: the picture's to the sequence's, the sequence's to the default
  /// matrices. A stream that signals no matrix at all is flat instead, which is a different thing
  /// from the default matrices and the common case.
  /// </summary>
  public static (byte[][] Lists4x4, byte[][] Lists8x8) Resolve(
    H264ScalingMatrix? sequence, H264ScalingMatrix? picture)
  {
    if (sequence == null && picture == null)
      return (Uniform(Lists, 16), Uniform(Kept8x8, 64));

    var intra4x4 = H264ResidualTables.DefaultScaling4x4[0];
    var inter4x4 = H264ResidualTables.DefaultScaling4x4[1];
    var intra8x8 = H264ResidualTables.DefaultScaling8x8[0];
    var inter8x8 = H264ResidualTables.DefaultScaling8x8[1];

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

/// <summary>
/// The scales the residual path multiplies levels by, with the picture's scaling matrices already
/// folded in. A flat matrix reproduces the unscaled tables exactly, so folding them in
/// unconditionally costs one pass over the QP range and leaves one form to apply rather than a
/// scaled path and an unscaled one beside it.
/// </summary>
internal sealed class H264Dequant
{
  private const int QpRange = 52;

  /// <summary>What a flat list contributes, and so what the caller divides back out.</summary>
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

  /// <summary>
  /// The unscaled table holds one row of the 4x4 twice over, since the scale repeats every two
  /// rows, and is indexed accordingly. A scaling list does not repeat, so this spreads it back out
  /// to all sixteen positions.
  /// </summary>
  private static int[][] Scale4x4(byte[] list)
  {
    var table = new int[QpRange][];
    for (var qp = 0; qp < QpRange; qp++)
    {
      var scale = H264ResidualTables.DequantCoeff4x4[qp];
      var row = new int[16];
      for (var i = 0; i < 16; i++)
        row[i] = list[i] * scale[i & 7];

      table[qp] = row;
    }

    return table;
  }

  /// <summary>
  /// The 8x8 table repeats every six QPs rather than carrying the octave shift the 4x4 one does,
  /// so the caller applies that shift itself and this stays the scale within one octave.
  /// </summary>
  private static int[][] Scale8x8(byte[] list)
  {
    var table = new int[QpRange][];
    for (var qp = 0; qp < QpRange; qp++)
    {
      var scale = H264ResidualTables.DequantCoeff8x8[qp];
      var row = new int[64];
      for (var i = 0; i < 64; i++)
        row[i] = list[i] * (scale[i] >> Shift);

      table[qp] = row;
    }

    return table;
  }
}
