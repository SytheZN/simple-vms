using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

/// <summary>
/// Block categories, numbered as the generated context-offset tables index them.
/// </summary>
internal static class H264Category
{
  public const int LumaDirect = 1;
  public const int LumaAlternating = 2;
  public const int Luma = 3;
  public const int ChromaDirect = 4;
  public const int ChromaAlternating = 5;
  public const int Luma8x8 = 6;
}

/// <summary>
/// Reads one residual block into the sparse pair of arrays everything downstream takes: where the
/// coefficients are and what they are. Nothing dense is built, so both the read and every pass
/// after it cost what the block actually carries rather than what it could.
///
/// One reader serves a stream: the significance walk writes its positions before it reads them.
/// </summary>
internal sealed class H264ResidualReader
{
  private IReconstructionObserver? _observer;

  internal void Observe(IReconstructionObserver observer) => _observer = observer;

  private readonly byte[] _positions = new byte[64];

  private const int CtxCodedBlockFlag = 85;
  private const int CtxSignificant = 105;
  private const int CtxLast = 166;
  private const int CtxLevelOne = 227;
  private const int CtxLevelAbs = 232;

  /// <summary>
  /// The 8x8 block shares none of the 4x4 contexts. Sixty-four positions would want far more
  /// significance contexts than the standard spends, so it has its own smaller set and reaches
  /// them through a table rather than by scan position.
  /// </summary>
  private const int CtxSignificant8x8 = 402;
  private const int CtxLast8x8 = 417;
  private const int CtxLevelOne8x8 = 426;
  private const int CtxLevelAbs8x8 = 431;

  /// <summary>
  /// A 4x4 category's context increment is its scan position, which the run takes as a table so the
  /// 8x8 category's real maps need no second spelling of the walk.
  /// </summary>
  private static readonly byte[] ScanPosition = BuildScanPosition();

  private static byte[] BuildScanPosition()
  {
    var positions = new byte[64];
    for (byte i = 0; i < positions.Length; i++)
      positions[i] = i;

    return positions;
  }

  /// <summary>
  /// Longest a level's unary prefix runs before it hands over to an exponential suffix.
  /// </summary>
  private const int PrefixLimit = 14;

  /// <summary>
  /// Wide enough that a suffix reaching it means the arithmetic decoder has already lost sync,
  /// since no legal level needs anything like this many bins.
  /// </summary>
  private const int SuffixLimit = 32;

  /// <summary>
  /// Returns how many coefficients were written. Zero means the block carries none at all, which
  /// is the common case and the one the coded-block flag settles in a single bin.
  ///
  /// <paramref name="scan"/> turns a scan position into whatever the caller indexes by - a
  /// position inside the block for the ordinary categories, or which block the coefficient belongs
  /// to for the two that carry direct terms.
  /// </summary>
  public int Read(
    CabacEngine cabac, int category, int condA, int condB,
    ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels)
  {
    _observer?.Begin(ReconstructionPhase.Significance);

    var flag = CtxCodedBlockFlag + H264ResidualTables.CategoryOffsetCbf[category]
      + condA + 2 * condB;

    var last = H264ResidualTables.CategoryMaxPosition[category];
    var significant = CtxSignificant + H264ResidualTables.CategoryOffsetMap[category];
    var terminal = CtxLast + H264ResidualTables.CategoryOffsetLast[category];

    Span<byte> positions = _positions;

    // The 4x4 categories take their contexts from the scan position itself, which the run reads
    // through the same table the 8x8 category needs so that neither has a shape of its own.
    var count = cabac.DecodeSignificanceRun(
      flag, significant, terminal, ScanPosition, ScanPosition, last, positions, out var ended);

    if (count < 0)
    {
      _observer?.End(ReconstructionPhase.Significance);
      return 0;
    }

    // Reaching the final position without a terminator makes it significant by implication.
    if (!ended)
      positions[count++] = (byte)last;

    _observer?.End(ReconstructionPhase.Significance);
    _observer?.Begin(ReconstructionPhase.Levels);

    ReadLevels(
      cabac,
      CtxLevelOne + H264ResidualTables.CategoryOffsetOne[category],
      CtxLevelAbs + H264ResidualTables.CategoryOffsetAbs[category],
      H264ResidualTables.CategoryMaxContext2[category],
      positions[..count], scan, occupied, levels);

    _observer?.End(ReconstructionPhase.Levels);
    return count;
  }

  /// <summary>
  /// The 8x8 luma block, which differs from the 4x4 categories in more than its size: CABAC codes
  /// no coded-block flag for it, since the coded block pattern already said the block carries
  /// coefficients, and both significance contexts are read out of tables rather than being the
  /// scan position itself.
  /// </summary>
  public int Read8x8(
    CabacEngine cabac, ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels)
  {
    _observer?.Begin(ReconstructionPhase.Significance);

    var last = H264ResidualTables.CategoryMaxPosition[H264Category.Luma8x8];

    Span<byte> positions = _positions;

    // No coded block flag: the coded block pattern already said this one carries coefficients.
    var count = cabac.DecodeSignificanceRun(
      -1, CtxSignificant8x8, CtxLast8x8,
      H264ResidualTables.SignificantCoeffFlag8x8,
      H264ResidualTables.LastSignificantCoeffFlag8x8,
      last, positions, out var ended);

    if (!ended)
      positions[count++] = (byte)last;

    _observer?.End(ReconstructionPhase.Significance);
    _observer?.Begin(ReconstructionPhase.Levels);

    ReadLevels(
      cabac, CtxLevelOne8x8, CtxLevelAbs8x8,
      H264ResidualTables.CategoryMaxContext2[H264Category.Luma8x8],
      positions[..count], scan, occupied, levels);

    _observer?.End(ReconstructionPhase.Levels);
    return count;
  }

  /// <summary>
  /// Levels run backwards along the scan, and both their contexts follow what has already been
  /// read: one tracks how many ran to exactly one, the other how many ran past it. The first stops
  /// mattering the moment any coefficient exceeds one, which is why it collapses to a single
  /// context from there on.
  /// </summary>
  private static void ReadLevels(
    CabacEngine cabac, int one, int abs, int cap, ReadOnlySpan<byte> positions,
    ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels)
  {
    cabac.DecodeLevelRun(one, abs, cap, PrefixLimit, SuffixLimit, levels, positions.Length);

    for (var n = 0; n < positions.Length; n++)
      occupied[n] = scan[positions[n]];
  }
}
