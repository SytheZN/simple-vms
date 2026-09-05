using Utils;

namespace H264;

public static class ResidualCategory
{
  public const int LumaDirect = 1;
  public const int LumaAlternating = 2;
  public const int Luma = 3;
  public const int ChromaDirect = 4;
  public const int ChromaAlternating = 5;
  public const int Luma8x8 = 6;
}

public sealed class ResidualReader
{
  private IObserverHarness<ReconstructionPhase>? _observer;

  public void Observe(IObserverHarness<ReconstructionPhase> observer) => _observer = observer;

  private readonly byte[] _positions = new byte[64];

  private const int CtxCodedBlockFlag = 85;
  private const int CtxSignificant = 105;
  private const int CtxLast = 166;
  private const int CtxLevelOne = 227;
  private const int CtxLevelAbs = 232;

  private const int CtxSignificant8x8 = 402;
  private const int CtxLast8x8 = 417;
  private const int CtxLevelOne8x8 = 426;
  private const int CtxLevelAbs8x8 = 431;

  private static readonly byte[] ScanPosition = BuildScanPosition();

  private static byte[] BuildScanPosition()
  {
    var positions = new byte[64];
    for (byte i = 0; i < positions.Length; i++)
      positions[i] = i;

    return positions;
  }

  private const int PrefixLimit = 14;

  private const int SuffixLimit = 32;

  public int Read(
    CabacEngine cabac, int category, int condA, int condB,
    ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels)
  {
    var activity = 0;
    return ReadCore(cabac, category, condA, condB, scan, occupied, levels,
      emit: true, ref activity);
  }

  public int ReadActivity(
    CabacEngine cabac, int category, int condA, int condB, ref int activity)
  {
    return ReadCore(cabac, category, condA, condB, default, default, default,
      emit: false, ref activity);
  }

  private int ReadCore(
    CabacEngine cabac, int category, int condA, int condB,
    ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels,
    bool emit, ref int activity)
  {
    _observer?.Begin(ReconstructionPhase.Significance);

    var flag = CtxCodedBlockFlag + ResidualTables.CategoryOffsetCbf[category]
      + condA + 2 * condB;

    var last = ResidualTables.CategoryMaxPosition[category];
    var significant = CtxSignificant + ResidualTables.CategoryOffsetMap[category];
    var terminal = CtxLast + ResidualTables.CategoryOffsetLast[category];

    Span<byte> positions = _positions;

    var count = cabac.DecodeSignificanceRun(
      flag, significant, terminal, ScanPosition, ScanPosition, last, positions, out var ended);

    if (count < 0)
    {
      _observer?.End(ReconstructionPhase.Significance);
      return 0;
    }

    if (!ended)
      positions[count++] = (byte)last;

    _observer?.End(ReconstructionPhase.Significance);
    _observer?.Begin(ReconstructionPhase.Levels);

    ReadLevels(
      cabac,
      CtxLevelOne + ResidualTables.CategoryOffsetOne[category],
      CtxLevelAbs + ResidualTables.CategoryOffsetAbs[category],
      ResidualTables.CategoryMaxContext2[category],
      positions[..count], scan, occupied, levels, emit, ref activity, lowFreqLimit: 4);

    _observer?.End(ReconstructionPhase.Levels);
    return count;
  }

  public int Read8x8(
    CabacEngine cabac, ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels)
  {
    var activity = 0;
    return Read8x8Core(cabac, scan, occupied, levels, emit: true, ref activity);
  }

  public int Read8x8Activity(CabacEngine cabac, ref int activity) =>
    Read8x8Core(cabac, default, default, default, emit: false, ref activity);

  private int Read8x8Core(
    CabacEngine cabac, ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels,
    bool emit, ref int activity)
  {
    _observer?.Begin(ReconstructionPhase.Significance);

    var last = ResidualTables.CategoryMaxPosition[ResidualCategory.Luma8x8];

    Span<byte> positions = _positions;

    var count = cabac.DecodeSignificanceRun(
      -1, CtxSignificant8x8, CtxLast8x8,
      ResidualTables.SignificantCoeffFlag8x8,
      ResidualTables.LastSignificantCoeffFlag8x8,
      last, positions, out var ended);

    if (!ended)
      positions[count++] = (byte)last;

    _observer?.End(ReconstructionPhase.Significance);
    _observer?.Begin(ReconstructionPhase.Levels);

    ReadLevels(
      cabac, CtxLevelOne8x8, CtxLevelAbs8x8,
      ResidualTables.CategoryMaxContext2[ResidualCategory.Luma8x8],
      positions[..count], scan, occupied, levels, emit, ref activity, lowFreqLimit: 16);

    _observer?.End(ReconstructionPhase.Levels);
    return count;
  }

  private static void ReadLevels(
    CabacEngine cabac, int one, int abs, int cap, ReadOnlySpan<byte> positions,
    ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels,
    bool emit, ref int activity, int lowFreqLimit)
  {
    var sum = cabac.DecodeLevelRun(
      one, abs, cap, PrefixLimit, SuffixLimit, levels, positions.Length);

    if (!emit)
    {
      activity += ActivityWeighting.Weigh(sum, positions, lowFreqLimit);
      return;
    }

    for (var n = 0; n < positions.Length; n++)
      occupied[n] = scan[positions[n]];
  }
}
