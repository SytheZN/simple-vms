using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

/// <summary>
/// Parses residual_coding, reporting where it put levels so nothing downstream has to search for
/// them. One reader serves a stream: its working buffers are written before they are read on every
/// path, so holding them here spares the zeroing a per-call stackalloc would repeat per sub-block.
/// </summary>
internal sealed class H265ResidualReader
{
  private IReconstructionObserver? _observer;

  internal void Observe(IReconstructionObserver observer) => _observer = observer;

  private readonly bool[] _codedSubBlocks = new bool[64];
  private readonly byte[] _positions = new byte[16];
  private readonly int[] _levels = new int[16];

  private const int CtxSigCoeff = 18;
  private const int CtxLastXPrefix = 60;
  private const int CtxLastYPrefix = 78;
  private const int CtxGreater1 = 96;
  private const int CtxGreater2 = 120;
  private const int CtxTransformSkipLuma = 135;
  private const int CtxTransformSkipChroma = 136;
  private const int CtxCodedSubBlock = 152;

  /// <summary>
  /// Levels come out as two parallel runs - <paramref name="occupied"/> holding each one's position
  /// in the block and <paramref name="values"/> its signed level, both in decoding order. Nothing
  /// downstream has to search for them, and a block is never laid out densely unless a path that
  /// works in the sample domain asks for it.
  ///
  /// Returns false when a decoded value lands outside the range the syntax allows, which only
  /// happens once CABAC has lost sync. Reporting it here names the element that first went wrong
  /// instead of leaving a spurious end-of-slice to surface much later.
  /// </summary>
  public bool Read(
    CabacEngine cabac, int log2TrSize, int cIdx, H265ScanIdx scanIdx,
    bool transformSkipEnabled, bool signDataHiding,
    Span<ushort> occupied, Span<int> values, out int levelCount, out bool transformSkip)
  {
    var size = 1 << log2TrSize;
    levelCount = 0;

    _observer?.Begin(ReconstructionPhase.Last);

    transformSkip = transformSkipEnabled && log2TrSize == 2
      && cabac.DecodeDecision(cIdx == 0 ? CtxTransformSkipLuma : CtxTransformSkipChroma) == 1;

    var (lastX, lastY) = ReadLastPosition(cabac, log2TrSize, cIdx, scanIdx);

    _observer?.End(ReconstructionPhase.Last);

    if (lastX >= size || lastY >= size)
      return false;

    var log2SubBlockWidth = log2TrSize - 2;
    var subBlockWidth = 1 << log2SubBlockWidth;
    var subBlockScan = H265ScanOrder.For(scanIdx, log2SubBlockWidth);
    var scan = H265ScanOrder.For(scanIdx, 2);

    // The scan tables hold bytes; the sub-block index counts down to zero, which a byte cannot do.
    int lastSubBlock = H265ScanOrder.Inverse(scanIdx, log2SubBlockWidth)
      [(lastY >> 2) * subBlockWidth + (lastX >> 2)];
    int lastPosInSubBlock = H265ScanOrder.Inverse(scanIdx, 2)
      [((lastY & 3) << 2) | (lastX & 3)];

    Span<bool> codedSubBlocks = _codedSubBlocks;
    codedSubBlocks[..(subBlockWidth * subBlockWidth)].Clear();
    var greater1Ctx = 1;
    var dcContext = CtxSigCoeff + (cIdx == 0 ? 0 : 27);
    Span<byte> positions = _positions;

    for (var i = lastSubBlock; i >= 0; i--)
    {
      _observer?.Begin(ReconstructionPhase.Significance);

      var subBlockRaster = subBlockScan[i];
      var xS = subBlockRaster & (subBlockWidth - 1);
      var yS = subBlockRaster >> log2SubBlockWidth;

      var right = xS + 1 < subBlockWidth && codedSubBlocks[yS * subBlockWidth + xS + 1] ? 1 : 0;
      var below = yS + 1 < subBlockWidth && codedSubBlocks[(yS + 1) * subBlockWidth + xS] ? 1 : 0;

      bool coded;
      if (i == lastSubBlock || i == 0)
      {
        coded = true;
      }
      else
      {
        var ctx = CtxCodedSubBlock + Math.Min(right + below, 1) + (cIdx > 0 ? 2 : 0);
        coded = cabac.DecodeDecision(ctx) == 1;
      }

      codedSubBlocks[yS * subBlockWidth + xS] = coded;
      if (!coded)
      {
        _observer?.End(ReconstructionPhase.Significance);
        continue;
      }

      // Everything the significance context depends on except the coefficient's own position is
      // settled for the whole sub-block, so it resolves to one base and one table here.
      ReadOnlySpan<byte> sigOffsets;
      int sigBase;
      var dcSubBlock = false;

      if (log2TrSize == 2)
      {
        sigOffsets = SigCtxByScan.AsSpan(Offsets((int)scanIdx, Small4x4), 16);
        sigBase = dcContext;
      }
      else
      {
        sigOffsets = SigCtxByScan.AsSpan(Offsets((int)scanIdx, right + (below << 1)), 16);
        dcSubBlock = (xS | yS) == 0;
        sigBase = dcContext + (cIdx == 0
          ? (xS + yS > 0 ? 3 : 0)
            + (log2TrSize == 3 ? scanIdx == H265ScanIdx.Diagonal ? 9 : 15 : 21)
          : log2TrSize == 3 ? 9 : 12);
      }

      var inferDcSig = i < lastSubBlock && i > 0;
      var start = i == lastSubBlock ? lastPosInSubBlock - 1 : 15;

      var count = 0;
      if (i == lastSubBlock)
        positions[count++] = (byte)lastPosInSubBlock;

      // Every scan starts at the sub-block's own DC and is a permutation of its positions, so no
      // later one is it - which keeps the last step's two special cases out of the walk.
      //
      // Which context each flag uses is settled by its position alone, so the whole walk goes to the
      // engine as one run and its state never returns to memory in the middle of it.
      count = cabac.DecodeDecisionRun(sigBase, sigOffsets, start, positions, count);

      if (start >= 0)
      {
        positions[count] = 0;
        count += inferDcSig && count == 0
          ? 1
          : cabac.DecodeDecision(dcSubBlock ? dcContext : sigBase + sigOffsets[0]);
      }

      _observer?.End(ReconstructionPhase.Significance);

      if (count == 0) continue;

      if (!ReadLevels(
            cabac, cIdx, i, positions[..count], scan, ref greater1Ctx, signDataHiding,
            xS, yS, log2TrSize, occupied, values, ref levelCount))
        return false;
    }

    return true;
  }

  private static (int X, int Y) ReadLastPosition(
    CabacEngine cabac, int log2TrSize, int cIdx, H265ScanIdx scanIdx)
  {
    var (offset, shift) = cIdx == 0
      ? (3 * (log2TrSize - 2) + ((log2TrSize - 1) >> 2), (log2TrSize + 1) >> 2)
      : (15, log2TrSize - 2);

    var maxPrefix = (log2TrSize << 1) - 1;

    var xPrefix = 0;
    while (xPrefix < maxPrefix &&
           cabac.DecodeDecision(CtxLastXPrefix + offset + (xPrefix >> shift)) == 1)
      xPrefix++;

    var yPrefix = 0;
    while (yPrefix < maxPrefix &&
           cabac.DecodeDecision(CtxLastYPrefix + offset + (yPrefix >> shift)) == 1)
      yPrefix++;

    var x = Reconstruct(cabac, xPrefix);
    var y = Reconstruct(cabac, yPrefix);

    return scanIdx == H265ScanIdx.Vertical ? (y, x) : (x, y);
  }

  private static int Reconstruct(CabacEngine cabac, int prefix)
  {
    if (prefix <= 3) return prefix;

    var suffixBits = (prefix >> 1) - 1;
    var suffix = (int)cabac.DecodeBypassBits(suffixBits);
    return H265ResidualTables.MinInGroup[prefix] + suffix;
  }

  /// <summary>
  /// The position-dependent half of the significance context derivation, which depends only on the
  /// neighbouring sub-blocks' coded pattern and on where the coefficient sits within its sub-block
  /// - six bits in total.
  /// </summary>
  private static byte PatternOffset(int pattern, int raster)
  {
    var xP = raster & 3;
    var yP = raster >> 2;
    return (byte)(pattern switch
    {
      0 => xP + yP == 0 ? 2 : xP + yP < 3 ? 1 : 0,
      1 => yP == 0 ? 2 : yP == 1 ? 1 : 0,
      2 => xP == 0 ? 2 : xP == 1 ? 1 : 0,
      _ => 2,
    });
  }

  /// <summary>
  /// The same offsets read by scan position rather than by raster position, with the 4x4 block's
  /// own map as a fifth pattern. The walk knows a coefficient by its place in the scan, so composing
  /// the two tables once here spares it a dependent lookup on every flag it decodes - which is a
  /// load the whole decode waits on, since the context it leads to decides the range.
  /// </summary>
  private static readonly byte[] SigCtxByScan = BuildSigCtxByScan();

  private const int Patterns = 5;
  private const int Small4x4 = 4;

  private static byte[] BuildSigCtxByScan()
  {
    var table = new byte[3 * Patterns * 16];

    for (var scanIdx = 0; scanIdx < 3; scanIdx++)
    {
      var scan = H265ScanOrder.For((H265ScanIdx)scanIdx, 2);

      for (var pattern = 0; pattern < Patterns; pattern++)
        for (var n = 0; n < 16; n++)
          table[Offsets(scanIdx, pattern) + n] = pattern == Small4x4
            ? H265ResidualTables.SigCtxMap4x4[scan[n]]
            : PatternOffset(pattern, scan[n]);
    }

    return table;
  }

  private static int Offsets(int scanIdx, int pattern) => (scanIdx * Patterns + pattern) * 16;

  /// <summary>
  /// Sign data hiding infers the lowest-frequency sign in a sub-block from the parity of its
  /// absolute level sum. <paramref name="positions"/> holds scan positions in decoding order,
  /// highest first.
  /// </summary>
  private bool ReadLevels(
    CabacEngine cabac, int cIdx, int subBlock, ReadOnlySpan<byte> positions, byte[] scan,
    ref int greater1Ctx, bool signDataHiding, int xS, int yS,
    int log2Size, Span<ushort> occupied, Span<int> values, ref int levelCount)
  {
    _observer?.Begin(ReconstructionPhase.Levels);

    var ctxSet = (subBlock == 0 || cIdx > 0) ? 0 : 2;
    if (greater1Ctx == 0) ctxSet++;
    greater1Ctx = 1;

    var greater1Base = CtxGreater1 + (cIdx == 0 ? 0 : 16) + ctxSet * 4;
    var firstGreater1 = -1;
    Span<int> levels = _levels;

    var toDecode = Math.Min(8, positions.Length);

    // Both updates are masked rather than branched for the same reason the significance walk is:
    // the flag is arithmetic-coded. "started" is all ones only on the flag that first sets the
    // index, and "bin - 1" is all ones only while the run of ones has not begun.
    for (var n = 0; n < toDecode; n++)
    {
      var bin = cabac.DecodeDecision(greater1Base + Math.Min(3, greater1Ctx));
      levels[n] = bin + 1;

      var started = (firstGreater1 >> 31) & -bin;
      firstGreater1 += started & (n - firstGreater1);

      var carry = (-greater1Ctx >> 31) & 1;
      greater1Ctx = (greater1Ctx + carry) & (bin - 1);
    }
    for (var n = toDecode; n < positions.Length; n++)
      levels[n] = 1;

    if (firstGreater1 >= 0)
    {
      var ctx = CtxGreater2 + (cIdx == 0 ? 0 : 4) + ctxSet;
      levels[firstGreater1] += cabac.DecodeDecision(ctx);
    }

    var hidden = signDataHiding && positions[0] - positions[^1] > 3;

    var signCount = hidden ? positions.Length - 1 : positions.Length;
    var signs = cabac.DecodeBypassBits(signCount);

    var riceParam = 0;
    var sum = 0;

    // Only the first eight positions carry a decoded magnitude, so only they can already be at the
    // level that says a remainder follows. Past them the level is one and the threshold is one, so
    // the remainder is unconditional and the comparison it came from is not worth making.
    for (var n = 0; n < toDecode; n++)
    {
      if (levels[n] == (n == firstGreater1 ? 3 : 2)
          && !Extend(cabac, levels, n, ref riceParam))
        return false;

      sum += levels[n];
    }

    for (var n = toDecode; n < positions.Length; n++)
    {
      if (!Extend(cabac, levels, n, ref riceParam)) return false;
      sum += levels[n];
    }

    _observer?.End(ReconstructionPhase.Levels);
    _observer?.Begin(ReconstructionPhase.Emit);

    // A sub-block's corner in the transform block, so a position within it costs a shift and two
    // adds rather than reconstructing its coordinates and multiplying by the stride.
    var origin = ((yS << 2) << log2Size) + (xS << 2);

    // The signs arrive as one field, highest first, which is the order they are spent in. Pushing it
    // up against the top of the word leaves each one in the sign bit as its turn comes, rather than
    // reaching back down for it by a distance that has to be worked out every time.
    var pending = signCount == 0 ? 0u : signs << (32 - signCount);

    for (var n = 0; n < signCount; n++, pending <<= 1)
    {
      var raster = scan[positions[n]];
      occupied[levelCount] = (ushort)(origin + ((raster >> 2) << log2Size) + (raster & 3));
      values[levelCount] = (int)pending < 0 ? -levels[n] : levels[n];
      levelCount++;
    }

    // The hidden sign is the last one decoded, and stands in the parity of the levels it follows.
    if (hidden)
    {
      var last = positions.Length - 1;
      var raster = scan[positions[last]];
      occupied[levelCount] = (ushort)(origin + ((raster >> 2) << log2Size) + (raster & 3));
      values[levelCount] = (sum & 1) == 1 ? -levels[last] : levels[last];
      levelCount++;
    }

    _observer?.End(ReconstructionPhase.Emit);
    return true;
  }

  /// <summary>
  /// Adds the remainder a level at its threshold carries, and moves the Rice parameter along with
  /// it. False means the value ran past what a coefficient can hold, which only happens once CABAC
  /// has lost sync.
  /// </summary>
  private static bool Extend(CabacEngine cabac, Span<int> levels, int n, ref int riceParam)
  {
    // A prefix this long already describes a level beyond the 16-bit coefficient range.
    const int maxPrefix = 20;

    var remaining = cabac.DecodeBypassRice(riceParam, maxPrefix);
    if (remaining < 0) return false;

    var level = levels[n] + remaining;
    if (level > 32767) return false;

    levels[n] = level;
    if (level > 3 * (1 << riceParam))
      riceParam = Math.Min(riceParam + 1, 4);

    return true;
  }

}
