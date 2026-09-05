using System.Numerics;
using Utils;

namespace H265;

public sealed class ResidualReader
{
  private IObserverHarness<ReconstructionPhase>? _observer;

  public void Observe(IObserverHarness<ReconstructionPhase> observer) => _observer = observer;

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
  private const int RicePrefixMax = 20;

  public bool Read(
    CabacEngine cabac, int log2TrSize, int cIdx, ScanIdx scanIdx,
    bool transformSkipEnabled, bool signDataHiding,
    Span<ushort> occupied, Span<int> values, out int levelCount, out bool transformSkip)
  {
    levelCount = 0;
    var activity = 0;
    return ReadCore(cabac, log2TrSize, cIdx, scanIdx, transformSkipEnabled, signDataHiding,
      emit: true, occupied, values, ref levelCount, ref activity, out transformSkip);
  }

  public bool ReadActivity(
    CabacEngine cabac, int log2TrSize, int cIdx, ScanIdx scanIdx,
    bool transformSkipEnabled, bool signDataHiding, ref int activity)
  {
    var levelCount = 0;
    return ReadCore(cabac, log2TrSize, cIdx, scanIdx, transformSkipEnabled, signDataHiding,
      emit: false, default, default, ref levelCount, ref activity, out _);
  }

  private bool ReadCore(
    CabacEngine cabac, int log2TrSize, int cIdx, ScanIdx scanIdx,
    bool transformSkipEnabled, bool signDataHiding, bool emit,
    Span<ushort> occupied, Span<int> values, ref int levelCount, ref int activity,
    out bool transformSkip)
  {
    if (log2TrSize == 2)
      return Read4x4(cabac, cIdx, scanIdx, transformSkipEnabled, signDataHiding,
        emit, occupied, values, ref levelCount, ref activity, out transformSkip);

    transformSkip = false;

    if (log2TrSize == 3)
      return Read8x8(cabac, cIdx, scanIdx, signDataHiding,
        emit, occupied, values, ref levelCount, ref activity);

    var size = 1 << log2TrSize;

    _observer?.Begin(ReconstructionPhase.Last);

    var (lastX, lastY) = ReadLastPosition(cabac, log2TrSize, cIdx, scanIdx);

    _observer?.End(ReconstructionPhase.Last);

    if (lastX >= size || lastY >= size)
      return false;

    var log2SubBlockWidth = log2TrSize - 2;
    var subBlockWidth = 1 << log2SubBlockWidth;
    var subBlockScan = ScanOrder.For(scanIdx, log2SubBlockWidth);

    int lastSubBlock = ScanOrder.Inverse(scanIdx, log2SubBlockWidth)
      [(lastY >> 2) * subBlockWidth + (lastX >> 2)];
    int lastPosInSubBlock = ScanOrder.Inverse(scanIdx, 2)
      [((lastY & 3) << 2) | (lastX & 3)];

    var codedSubBlocks = 0UL;
    var greater1Ctx = 1;
    var dcContext = CtxSigCoeff + (cIdx == 0 ? 0 : 27);
    var codedCtxBase = CtxCodedSubBlock + (cIdx > 0 ? 2 : 0);
    var sigConstant = dcContext + (cIdx == 0 ? 21 : 12);
    Span<byte> positions = _positions;

    for (var i = lastSubBlock; i >= 0; i--)
    {
      _observer?.Begin(ReconstructionPhase.Significance);

      var subBlockRaster = subBlockScan[i];
      var xS = subBlockRaster & (subBlockWidth - 1);
      var yS = subBlockRaster >> log2SubBlockWidth;

      var rightIn = (int)((uint)(xS + 1 - subBlockWidth) >> 31);
      var belowIn = (int)((uint)(yS + 1 - subBlockWidth) >> 31);
      var right = (int)(codedSubBlocks >> ((subBlockRaster + 1) & 63)) & 1 & rightIn;
      var below = (int)(codedSubBlocks >> ((subBlockRaster + subBlockWidth) & 63)) & 1 & belowIn;

      bool coded;
      if (i == lastSubBlock || i == 0)
      {
        coded = true;
      }
      else
      {
        coded = cabac.DecodeDecision(codedCtxBase + Math.Min(right + below, 1)) == 1;
      }

      codedSubBlocks |= (coded ? 1UL : 0UL) << subBlockRaster;
      if (!coded)
      {
        _observer?.End(ReconstructionPhase.Significance);
        continue;
      }

      var sigOffsets =
        (ReadOnlySpan<byte>)SigCtxByScan.AsSpan(Offsets((int)scanIdx, right + (below << 1)), 16);
      var dcSubBlock = i == 0;
      var sigBase = sigConstant + (cIdx == 0 && !dcSubBlock ? 3 : 0);

      var inferDcSig = i < lastSubBlock && i > 0;
      var start = i == lastSubBlock ? lastPosInSubBlock - 1 : 15;

      var count = 0;
      if (i == lastSubBlock)
        positions[count++] = (byte)lastPosInSubBlock;

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
            cabac, cIdx, i, positions[..count], scanIdx, ref greater1Ctx, signDataHiding,
            xS, yS, log2TrSize, emit, occupied, values, ref levelCount, ref activity))
        return false;
    }

    return true;
  }

  private bool Read8x8(
    CabacEngine cabac, int cIdx, ScanIdx scanIdx, bool signDataHiding, bool emit,
    Span<ushort> occupied, Span<int> values, ref int levelCount, ref int activity)
  {
    _observer?.Begin(ReconstructionPhase.Last);

    var offset = cIdx == 0 ? 3 : 15;

    var xPrefix = 0;
    while (xPrefix < 5 && cabac.DecodeDecision(CtxLastXPrefix + offset + (xPrefix >> 1)) == 1)
      xPrefix++;

    var yPrefix = 0;
    while (yPrefix < 5 && cabac.DecodeDecision(CtxLastYPrefix + offset + (yPrefix >> 1)) == 1)
      yPrefix++;

    var x = Reconstruct(cabac, xPrefix);
    var y = Reconstruct(cabac, yPrefix);
    var (lastX, lastY) = scanIdx == ScanIdx.Vertical ? (y, x) : (x, y);

    _observer?.End(ReconstructionPhase.Last);

    var subBlockScan = ScanOrder.For(scanIdx, 1);

    int lastSubBlock = ScanOrder.Inverse(scanIdx, 1)[((lastY >> 2) << 1) | (lastX >> 2)];
    int lastPosInSubBlock = ScanOrder.Inverse(scanIdx, 2)[((lastY & 3) << 2) | (lastX & 3)];

    var codedSubBlocks = 0UL;
    var greater1Ctx = 1;
    var dcContext = CtxSigCoeff + (cIdx == 0 ? 0 : 27);
    var codedCtxBase = CtxCodedSubBlock + (cIdx > 0 ? 2 : 0);
    var sigConstant = dcContext + (cIdx == 0 ? scanIdx == ScanIdx.Diagonal ? 9 : 15 : 9);
    Span<byte> positions = _positions;

    for (var i = lastSubBlock; i >= 0; i--)
    {
      _observer?.Begin(ReconstructionPhase.Significance);

      var subBlockRaster = subBlockScan[i];
      var xS = subBlockRaster & 1;
      var yS = subBlockRaster >> 1;

      var right = (int)(codedSubBlocks >> (subBlockRaster + 1)) & 1 & (xS ^ 1);
      var below = (int)(codedSubBlocks >> (subBlockRaster + 2)) & 1 & (yS ^ 1);

      bool coded;
      if (i == lastSubBlock || i == 0)
      {
        coded = true;
      }
      else
      {
        coded = cabac.DecodeDecision(codedCtxBase + Math.Min(right + below, 1)) == 1;
      }

      codedSubBlocks |= (coded ? 1UL : 0UL) << subBlockRaster;
      if (!coded)
      {
        _observer?.End(ReconstructionPhase.Significance);
        continue;
      }

      var sigOffsets =
        (ReadOnlySpan<byte>)SigCtxByScan.AsSpan(Offsets((int)scanIdx, right + (below << 1)), 16);
      var dcSubBlock = i == 0;
      var sigBase = sigConstant + (cIdx == 0 && !dcSubBlock ? 3 : 0);

      var inferDcSig = i < lastSubBlock && i > 0;
      var start = i == lastSubBlock ? lastPosInSubBlock - 1 : 15;

      var count = 0;
      if (i == lastSubBlock)
        positions[count++] = (byte)lastPosInSubBlock;

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
            cabac, cIdx, i, positions[..count], scanIdx, ref greater1Ctx, signDataHiding,
            xS, yS, 3, emit, occupied, values, ref levelCount, ref activity))
        return false;
    }

    return true;
  }

  private bool Read4x4(
    CabacEngine cabac, int cIdx, ScanIdx scanIdx,
    bool transformSkipEnabled, bool signDataHiding, bool emit,
    Span<ushort> occupied, Span<int> values, ref int levelCount, ref int activity,
    out bool transformSkip)
  {
    _observer?.Begin(ReconstructionPhase.Last);

    transformSkip = transformSkipEnabled
      && cabac.DecodeDecision(cIdx == 0 ? CtxTransformSkipLuma : CtxTransformSkipChroma) == 1;

    var offset = cIdx == 0 ? 0 : 15;

    var xPrefix = 0;
    while (xPrefix < 3 && cabac.DecodeDecision(CtxLastXPrefix + offset + xPrefix) == 1)
      xPrefix++;

    var yPrefix = 0;
    while (yPrefix < 3 && cabac.DecodeDecision(CtxLastYPrefix + offset + yPrefix) == 1)
      yPrefix++;

    var (lastX, lastY) = scanIdx == ScanIdx.Vertical ? (yPrefix, xPrefix) : (xPrefix, yPrefix);

    _observer?.End(ReconstructionPhase.Last);

    var lastPos = ScanOrder.Inverse(scanIdx, 2)[(lastY << 2) | lastX];

    _observer?.Begin(ReconstructionPhase.Significance);

    var dcContext = CtxSigCoeff + (cIdx == 0 ? 0 : 27);
    ReadOnlySpan<byte> sigOffsets = SigCtxByScan.AsSpan(Offsets((int)scanIdx, Small4x4), 16);
    Span<byte> positions = _positions;

    positions[0] = (byte)lastPos;
    var count = cabac.DecodeDecisionRun(dcContext, sigOffsets, lastPos - 1, positions, 1);

    if (lastPos > 0)
    {
      positions[count] = 0;
      count += cabac.DecodeDecision(dcContext + sigOffsets[0]);
    }

    _observer?.End(ReconstructionPhase.Significance);

    var greater1Ctx = 1;
    return ReadLevels(
      cabac, cIdx, 0, positions[..count], scanIdx, ref greater1Ctx, signDataHiding,
      0, 0, 2, emit, occupied, values, ref levelCount, ref activity);
  }

  private static (int X, int Y) ReadLastPosition(
    CabacEngine cabac, int log2TrSize, int cIdx, ScanIdx scanIdx)
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

    return scanIdx == ScanIdx.Vertical ? (y, x) : (x, y);
  }

  private static int Reconstruct(CabacEngine cabac, int prefix)
  {
    if (prefix <= 3) return prefix;

    var suffixBits = (prefix >> 1) - 1;
    var suffix = (int)cabac.DecodeBypassBits(suffixBits);
    return ResidualTables.MinInGroup[prefix] + suffix;
  }

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

  private static readonly byte[] SigCtxByScan = BuildSigCtxByScan();

  private const int Patterns = 5;
  private const int Small4x4 = 4;

  private static byte[] BuildSigCtxByScan()
  {
    var table = new byte[3 * Patterns * 16];

    for (var scanIdx = 0; scanIdx < 3; scanIdx++)
    {
      var scan = ScanOrder.For((ScanIdx)scanIdx, 2);

      for (var pattern = 0; pattern < Patterns; pattern++)
        for (var n = 0; n < 16; n++)
          table[Offsets(scanIdx, pattern) + n] = pattern == Small4x4
            ? ResidualTables.SigCtxMap4x4[scan[n]]
            : PatternOffset(pattern, scan[n]);
    }

    return table;
  }

  private static int Offsets(int scanIdx, int pattern) => (scanIdx * Patterns + pattern) * 16;

  private bool ReadLevels(
    CabacEngine cabac, int cIdx, int subBlock, ReadOnlySpan<byte> positions, ScanIdx scanIdx,
    ref int greater1Ctx, bool signDataHiding, int xS, int yS, int log2Size, bool emit,
    Span<ushort> occupied, Span<int> values, ref int levelCount, ref int activity)
  {
    _observer?.Begin(ReconstructionPhase.Levels);

    var ctxSet = (subBlock == 0 || cIdx > 0) ? 0 : 2;
    if (greater1Ctx == 0) ctxSet++;
    greater1Ctx = 1;

    var greater1Base = CtxGreater1 + (cIdx == 0 ? 0 : 16) + ctxSet * 4;
    var firstGreater1 = -1;
    Span<int> levels = _levels;

    var toDecode = Math.Min(8, positions.Length);

    var sum = positions.Length;
    var escapeMask = ((1 << positions.Length) - 1) & ~((1 << toDecode) - 1);

    for (var n = 0; n < toDecode; n++)
    {
      var bin = cabac.DecodeDecision(greater1Base + Math.Min(3, greater1Ctx));
      if (emit) levels[n] = bin + 1;
      sum += bin;
      escapeMask |= bin << n;

      var started = (firstGreater1 >> 31) & -bin;
      firstGreater1 += started & (n - firstGreater1);

      var carry = (-greater1Ctx >> 31) & 1;
      greater1Ctx = (greater1Ctx + carry) & (bin - 1);
    }
    if (emit)
      for (var n = toDecode; n < positions.Length; n++)
        levels[n] = 1;

    if (firstGreater1 >= 0)
    {
      var ctx = CtxGreater2 + (cIdx == 0 ? 0 : 4) + ctxSet;
      var greater2 = cabac.DecodeDecision(ctx);
      if (emit) levels[firstGreater1] += greater2;
      sum += greater2;
      escapeMask = greater2 == 1
        ? escapeMask | (1 << firstGreater1)
        : escapeMask & ~(1 << firstGreater1);
    }

    var hidden = signDataHiding && positions[0] - positions[^1] > 3;

    var signCount = hidden ? positions.Length - 1 : positions.Length;
    var signs = cabac.DecodeBypassBits(signCount);

    var riceParam = 0;
    while (escapeMask != 0)
    {
      var n = BitOperations.TrailingZeroCount(escapeMask);
      escapeMask &= escapeMask - 1;

      var remaining = cabac.DecodeBypassRice(riceParam, RicePrefixMax);
      if (remaining < 0) return false;

      var level = (n >= toDecode ? 1 : n == firstGreater1 ? 3 : 2) + remaining;
      if (level > 32767) return false;

      if (level > 3 << riceParam)
        riceParam = Math.Min(riceParam + 1, 4);

      sum += remaining;
      if (emit) levels[n] = level;
    }

    _observer?.End(ReconstructionPhase.Levels);

    if (!emit)
    {
      activity += ActivityWeighting.Weigh(sum, positions, subBlock == 0 ? 4 : 0);
      return true;
    }

    _observer?.Begin(ReconstructionPhase.Emit);

    var scan = ScanOrder.For(scanIdx, 2);
    var origin = ((yS << 2) << log2Size) + (xS << 2);

    var pending = signCount == 0 ? 0u : signs << (32 - signCount);

    for (var n = 0; n < signCount; n++, pending <<= 1)
    {
      var raster = scan[positions[n]];
      occupied[levelCount] = (ushort)(origin + ((raster >> 2) << log2Size) + (raster & 3));
      values[levelCount] = (int)pending < 0 ? -levels[n] : levels[n];
      levelCount++;
    }

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

}
