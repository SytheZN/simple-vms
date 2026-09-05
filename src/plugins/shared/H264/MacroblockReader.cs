namespace H264;

public enum MbKind { Intra4x4, Intra8x8, Intra16x16, Pcm }

public struct Macroblock
{
  public MbKind Kind;
  public int CbpLuma;
  public int CbpChroma;
  public int Predicted16x16Mode;
  public int ChromaPredMode;
  public bool Transform8x8;

  public readonly bool IsNxN => Kind is MbKind.Intra4x4 or MbKind.Intra8x8;
}

public struct Neighbour
{
  private const byte AvailableBit = 1 << 0;
  private const byte IsNxNBit = 1 << 1;
  private const byte SkippedBit = 1 << 2;
  private const byte DirectBit = 1 << 3;
  private const byte ChromaPredModeNonZeroBit = 1 << 4;
  private const byte Transform8x8Bit = 1 << 5;
  private const byte PcmBit = 1 << 6;
  private const byte DcCbfBit = 1 << 7;

  private const byte CbDcCbfBit = 1 << 0;
  private const byte CrDcCbfBit = 1 << 1;
  private const int CbpChromaShift = 2;
  private const byte CbpChromaMask = 0x3 << CbpChromaShift;
  private const int CbpLumaShift = 4;

  private byte _flags;
  private byte _flags2;
  public ushort LumaCbf;
  public byte CbCbf;
  public byte CrCbf;

  public bool Available
  {
    readonly get => (_flags & AvailableBit) != 0;
    set => _flags = value ? (byte)(_flags | AvailableBit) : (byte)(_flags & ~AvailableBit);
  }

  public bool IsNxN
  {
    readonly get => (_flags & IsNxNBit) != 0;
    set => _flags = value ? (byte)(_flags | IsNxNBit) : (byte)(_flags & ~IsNxNBit);
  }

  public bool Skipped
  {
    readonly get => (_flags & SkippedBit) != 0;
    set => _flags = value ? (byte)(_flags | SkippedBit) : (byte)(_flags & ~SkippedBit);
  }

  public bool Direct
  {
    readonly get => (_flags & DirectBit) != 0;
    set => _flags = value ? (byte)(_flags | DirectBit) : (byte)(_flags & ~DirectBit);
  }

  public bool ChromaPredModeNonZero
  {
    readonly get => (_flags & ChromaPredModeNonZeroBit) != 0;
    set => _flags = value
      ? (byte)(_flags | ChromaPredModeNonZeroBit)
      : (byte)(_flags & ~ChromaPredModeNonZeroBit);
  }

  public bool Transform8x8
  {
    readonly get => (_flags & Transform8x8Bit) != 0;
    set => _flags = value ? (byte)(_flags | Transform8x8Bit) : (byte)(_flags & ~Transform8x8Bit);
  }

  public bool Pcm
  {
    readonly get => (_flags & PcmBit) != 0;
    set => _flags = value ? (byte)(_flags | PcmBit) : (byte)(_flags & ~PcmBit);
  }

  public bool DcCbf
  {
    readonly get => (_flags & DcCbfBit) != 0;
    set => _flags = value ? (byte)(_flags | DcCbfBit) : (byte)(_flags & ~DcCbfBit);
  }

  public bool CbDcCbf
  {
    readonly get => (_flags2 & CbDcCbfBit) != 0;
    set => _flags2 = value ? (byte)(_flags2 | CbDcCbfBit) : (byte)(_flags2 & ~CbDcCbfBit);
  }

  public bool CrDcCbf
  {
    readonly get => (_flags2 & CrDcCbfBit) != 0;
    set => _flags2 = value ? (byte)(_flags2 | CrDcCbfBit) : (byte)(_flags2 & ~CrDcCbfBit);
  }

  public int CbpChroma
  {
    readonly get => (_flags2 & CbpChromaMask) >> CbpChromaShift;
    set => _flags2 = (byte)((_flags2 & ~CbpChromaMask) | (value << CbpChromaShift));
  }

  public int CbpLuma
  {
    readonly get => _flags2 >> CbpLumaShift;
    set => _flags2 = (byte)((_flags2 & ~(0xF << CbpLumaShift)) | (value << CbpLumaShift));
  }
}

public static class MacroblockReader
{
  private const int CtxMbTypeI = 3;
  private const int CtxIntraChromaPredMode = 64;
  private const int CtxPrevIntraPredMode = 68;
  private const int CtxRemIntraPredMode = 69;
  private const int CtxCbpLuma = 73;
  private const int CtxCbpChroma = 77;
  private const int CtxMbQpDelta = 60;
  private const int CtxTransform8x8 = 399;

  private const int RemIntraPredModeBits = 3;

  public static Macroblock ReadHeader(
    CabacEngine cabac, bool transform8x8Allowed,
    in Neighbour left, in Neighbour above,
    Span<sbyte> modes, ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes)
  {
    var ctxInc = (left.Available && !left.IsNxN ? 1 : 0)
               + (above.Available && !above.IsNxN ? 1 : 0);

    if (cabac.DecodeDecision(CtxMbTypeI + ctxInc) == 0)
      return ReadNxN(cabac, transform8x8Allowed, left, above, modes, leftModes, aboveModes);

    if (cabac.DecodeTerminate() == 1)
      return new Macroblock { Kind = MbKind.Pcm };

    var cbpLuma = cabac.DecodeDecision(CtxMbTypeI + 3) == 1 ? 15 : 0;

    var cbpChroma = 0;
    if (cabac.DecodeDecision(CtxMbTypeI + 4) == 1)
      cbpChroma = cabac.DecodeDecision(CtxMbTypeI + 5) == 1 ? 2 : 1;

    var mode = (cabac.DecodeDecision(CtxMbTypeI + 6) << 1)
             | cabac.DecodeDecision(CtxMbTypeI + 7);

    return new Macroblock
    {
      Kind = MbKind.Intra16x16,
      CbpLuma = cbpLuma,
      CbpChroma = cbpChroma,
      Predicted16x16Mode = mode,
      ChromaPredMode = ReadChromaPredMode(cabac, left, above),
    };
  }

  private static Macroblock ReadNxN(
    CabacEngine cabac, bool transform8x8Allowed,
    in Neighbour left, in Neighbour above,
    Span<sbyte> modes, ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes)
  {
    var transform8x8 = false;
    if (transform8x8Allowed)
    {
      var neighbours = (left.Available && left.Transform8x8 ? 1 : 0)
                     + (above.Available && above.Transform8x8 ? 1 : 0);

      transform8x8 = cabac.DecodeDecision(CtxTransform8x8 + neighbours) == 1;
    }

    ReadPredModes(
      cabac, modes, leftModes, aboveModes, left.Available, above.Available,
      transform8x8 ? 4 : 1);

    var chromaMode = ReadChromaPredMode(cabac, left, above);
    var (cbpLuma, cbpChroma) = ReadCodedBlockPattern(cabac, left, above);

    return new Macroblock
    {
      Kind = transform8x8 ? MbKind.Intra8x8 : MbKind.Intra4x4,
      CbpLuma = cbpLuma,
      CbpChroma = cbpChroma,
      ChromaPredMode = chromaMode,
      Transform8x8 = transform8x8,
    };
  }

  private static void ReadPredModes(
    CabacEngine cabac, Span<sbyte> modes,
    ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes,
    bool leftAvailable, bool aboveAvailable, int span)
  {
    for (var i = 0; i < 16; i += span)
    {
      var predicted = PredictedMode(i, modes, leftModes, aboveModes, leftAvailable, aboveAvailable);

      var remainder = cabac.DecodeFlagOrField(
        CtxPrevIntraPredMode, CtxRemIntraPredMode, RemIntraPredModeBits);

      var mode = remainder < 0
        ? predicted
        : (sbyte)(remainder < predicted ? remainder : remainder + 1);

      for (var j = 0; j < span; j++)
        modes[i + j] = mode;
    }
  }

  public static sbyte PredictedMode(
    int block, ReadOnlySpan<sbyte> modes,
    ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes,
    bool leftAvailable, bool aboveAvailable)
  {
    var l = BlockOrder.NeighbourLeft[block];
    var t = BlockOrder.NeighbourAbove[block];

    if ((l >= BlockOrder.Outside && !leftAvailable)
        || (t >= BlockOrder.Outside && !aboveAvailable))
      return 2;

    var left = l < BlockOrder.Outside ? modes[l] : leftModes[l - BlockOrder.Outside];
    var above = t < BlockOrder.Outside ? modes[t] : aboveModes[t - BlockOrder.Outside];
    return Math.Min(left, above);
  }

  public static int ReadChromaPredMode(
    CabacEngine cabac, in Neighbour left, in Neighbour above)
  {
    var ctxInc = (left.Available && left.ChromaPredModeNonZero && !left.Pcm ? 1 : 0)
               + (above.Available && above.ChromaPredModeNonZero && !above.Pcm ? 1 : 0);

    if (cabac.DecodeDecision(CtxIntraChromaPredMode + ctxInc) == 0)
      return 0;

    if (cabac.DecodeDecision(CtxIntraChromaPredMode + 3) == 0)
      return 1;

    return cabac.DecodeDecision(CtxIntraChromaPredMode + 3) == 0 ? 2 : 3;
  }

  public static (int Luma, int Chroma) ReadCodedBlockPattern(
    CabacEngine cabac, in Neighbour left, in Neighbour above)
  {
    var luma = 0;
    for (var i = 0; i < 4; i++)
    {
      var condA = LumaCondTerm(i, 0, luma, left, above);
      var condB = LumaCondTerm(i, 1, luma, left, above);
      if (cabac.DecodeDecision(CtxCbpLuma + condA + 2 * condB) == 1)
        luma |= 1 << i;
    }

    var chromaA = left.Available && (left.Pcm || left.CbpChroma != 0) ? 1 : 0;
    var chromaB = above.Available && (above.Pcm || above.CbpChroma != 0) ? 1 : 0;

    var chroma = 0;
    if (cabac.DecodeDecision(CtxCbpChroma + chromaA + 2 * chromaB) == 1)
    {
      var secondA = left.Available && (left.Pcm || left.CbpChroma == 2) ? 1 : 0;
      var secondB = above.Available && (above.Pcm || above.CbpChroma == 2) ? 1 : 0;
      chroma = cabac.DecodeDecision(CtxCbpChroma + 4 + secondA + 2 * secondB) == 1 ? 2 : 1;
    }

    return (luma, chroma);
  }

  private static int LumaCondTerm(
    int block, int direction, int currentLuma,
    in Neighbour left, in Neighbour above)
  {
    var isLeft = direction == 0;
    var inside = isLeft ? (block & 1) == 1 : block >= 2;

    if (inside)
    {
      var neighbourBlock = isLeft ? block - 1 : block - 2;
      return ((currentLuma >> neighbourBlock) & 1) != 0 ? 0 : 1;
    }

    var mb = isLeft ? left : above;
    if (!mb.Available || mb.Pcm) return 0;

    var mirrored = isLeft ? block + 1 : block + 2;
    return ((mb.CbpLuma >> mirrored) & 1) != 0 ? 0 : 1;
  }

  public static int ReadQpDelta(CabacEngine cabac, int previousDelta)
  {
    if (cabac.DecodeDecision(CtxMbQpDelta + (previousDelta != 0 ? 1 : 0)) == 0)
      return 0;

    var magnitude = 1;
    if (cabac.DecodeDecision(CtxMbQpDelta + 2) == 1)
    {
      magnitude++;
      while (magnitude < 88 && cabac.DecodeDecision(CtxMbQpDelta + 3) == 1)
        magnitude++;
    }

    return (magnitude & 1) == 1 ? (magnitude + 1) / 2 : -(magnitude / 2);
  }
}

public static class BlockOrder
{
  public static readonly (byte X, byte Y)[] Position = BuildPositions();

  public static readonly byte[] Index = BuildIndex();

  private static (byte X, byte Y)[] BuildPositions()
  {
    var positions = new (byte X, byte Y)[16];
    for (var i = 0; i < 16; i++)
    {
      var quadrant = i / 4;
      var sub = i % 4;
      positions[i] = (
        (byte)(quadrant % 2 * 2 + sub % 2),
        (byte)(quadrant / 2 * 2 + sub / 2));
    }
    return positions;
  }

  private static byte[] BuildIndex()
  {
    var map = new byte[16];
    for (byte i = 0; i < 16; i++)
    {
      var (x, y) = Position[i];
      map[y * 4 + x] = i;
    }
    return map;
  }

  public const int Outside = 16;

  public static readonly byte[] NeighbourLeft = Neighbour(left: true, morton: false);
  public static readonly byte[] NeighbourAbove = Neighbour(left: false, morton: false);

  public static readonly byte[] CbfLeft = Neighbour(left: true, morton: true);
  public static readonly byte[] CbfAbove = Neighbour(left: false, morton: true);

  private static byte[] Neighbour(bool left, bool morton)
  {
    var table = new byte[16];

    for (var block = 0; block < table.Length; block++)
    {
      var (bx, by) = Position[block];
      var inside = left ? bx > 0 : by > 0;

      table[block] = inside
        ? Index[left ? by * 4 + bx - 1 : (by - 1) * 4 + bx]
        : (byte)(Outside + (morton
          ? Index[left ? by * 4 + 3 : 3 * 4 + bx]
          : left ? by : bx));
    }

    return table;
  }
}
