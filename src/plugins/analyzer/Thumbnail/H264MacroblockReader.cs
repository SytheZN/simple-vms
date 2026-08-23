namespace Analyzer.Thumbnail;

internal enum H264MbKind { Intra4x4, Intra8x8, Intra16x16, Pcm }

internal struct H264Macroblock
{
  public H264MbKind Kind;
  public int CbpLuma;
  public int CbpChroma;
  public int Predicted16x16Mode;
  public int ChromaPredMode;
  public bool Transform8x8;

  public readonly bool IsNxN => Kind is H264MbKind.Intra4x4 or H264MbKind.Intra8x8;
}

/// <summary>
/// Neighbour state for context derivation. Contexts must match the encoder exactly - unlike
/// reconstruction, nothing here can be approximated without producing wrong bin values.
/// </summary>
internal struct H264Neighbour
{
  public bool Available;
  public bool IsNxN;
  public int CbpLuma;
  public int CbpChroma;
  public bool ChromaPredModeNonZero;
  public bool Transform8x8;

  /// <summary>
  /// Raw samples carry no coded state, so every context that asks a neighbour what it holds gets
  /// a fixed answer here rather than a real one - and not the same fixed answer each time.
  /// </summary>
  public bool Pcm;

  /// <summary>
  /// The direct-term block and the alternating blocks take their contexts from different places,
  /// so a neighbour has to remember them apart rather than as one "has coefficients".
  /// </summary>
  public bool DcCbf;
  public bool CbDcCbf;
  public bool CrDcCbf;

  public ushort LumaCbf;
  public byte CbCbf;
  public byte CrCbf;
}

internal static class H264MacroblockReader
{
  private const int CtxMbTypeI = 3;
  private const int CtxIntraChromaPredMode = 64;
  private const int CtxPrevIntraPredMode = 68;
  private const int CtxRemIntraPredMode = 69;
  private const int CtxCbpLuma = 73;
  private const int CtxCbpChroma = 77;
  private const int CtxMbQpDelta = 60;
  private const int CtxTransform8x8 = 399;

  /// <summary>Eight modes remain once the predicted one is taken out, so three bins name them.</summary>
  private const int RemIntraPredModeBits = 3;

  public static H264Macroblock ReadHeader(
    CabacEngine cabac, bool transform8x8Allowed,
    in H264Neighbour left, in H264Neighbour above,
    Span<sbyte> modes, ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes)
  {
    var ctxInc = (left.Available && !left.IsNxN ? 1 : 0)
               + (above.Available && !above.IsNxN ? 1 : 0);

    if (cabac.DecodeDecision(CtxMbTypeI + ctxInc) == 0)
      return ReadNxN(cabac, transform8x8Allowed, left, above, modes, leftModes, aboveModes);

    if (cabac.DecodeTerminate() == 1)
      return new H264Macroblock { Kind = H264MbKind.Pcm };

    var cbpLuma = cabac.DecodeDecision(CtxMbTypeI + 3) == 1 ? 15 : 0;

    var cbpChroma = 0;
    if (cabac.DecodeDecision(CtxMbTypeI + 4) == 1)
      cbpChroma = cabac.DecodeDecision(CtxMbTypeI + 5) == 1 ? 2 : 1;

    // The bin saying whether chroma carries AC shifts every later bin along by one, and the
    // context increment those bins take shifts with it - so both spellings land on the same pair
    // of contexts and neither branch has to know which happened.
    var mode = (cabac.DecodeDecision(CtxMbTypeI + 6) << 1)
             | cabac.DecodeDecision(CtxMbTypeI + 7);

    return new H264Macroblock
    {
      Kind = H264MbKind.Intra16x16,
      CbpLuma = cbpLuma,
      CbpChroma = cbpChroma,
      Predicted16x16Mode = mode,
      ChromaPredMode = ReadChromaPredMode(cabac, left, above),
    };
  }

  private static H264Macroblock ReadNxN(
    CabacEngine cabac, bool transform8x8Allowed,
    in H264Neighbour left, in H264Neighbour above,
    Span<sbyte> modes, ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes)
  {
    var transform8x8 = false;
    if (transform8x8Allowed)
    {
      var neighbours = (left.Available && left.Transform8x8 ? 1 : 0)
                     + (above.Available && above.Transform8x8 ? 1 : 0);

      transform8x8 = cabac.DecodeDecision(CtxTransform8x8 + neighbours) == 1;
    }

    // One mode per transform block either way, so the 8x8 macroblock reads a quarter as many and
    // gives each to all four of the 4x4 slots a later neighbour looks it up through.
    ReadPredModes(
      cabac, modes, leftModes, aboveModes, left.Available, above.Available,
      transform8x8 ? 4 : 1);

    var chromaMode = ReadChromaPredMode(cabac, left, above);
    var (cbpLuma, cbpChroma) = ReadCodedBlockPattern(cabac, left, above);

    return new H264Macroblock
    {
      Kind = transform8x8 ? H264MbKind.Intra8x8 : H264MbKind.Intra4x4,
      CbpLuma = cbpLuma,
      CbpChroma = cbpChroma,
      ChromaPredMode = chromaMode,
      Transform8x8 = transform8x8,
    };
  }

  /// <summary>
  /// Each block's mode is coded against a prediction from the blocks left of and above it. Why a
  /// neighbour is missing decides which of two rules produces that prediction: a macroblock
  /// outside the picture forces it to DC outright, while a macroblock that is merely coded
  /// without per-block modes contributes DC and still takes the smaller of the two. Collapsing
  /// the first rule into the second leaves every block down the left edge and along the top
  /// predicting from a mode the encoder never used - which the parse survives, since both
  /// spellings read the same bins, and the picture does not.
  ///
  /// The three remainder bins are context coded and share one context. Reading them as bypass
  /// costs nothing on the first block that takes them and desynchronises everything after it.
  /// </summary>
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

  /// <summary>
  /// Shared with the CAVLC reader, which codes the same prediction differently but derives it
  /// identically.
  /// </summary>
  internal static sbyte PredictedMode(
    int block, ReadOnlySpan<sbyte> modes,
    ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes,
    bool leftAvailable, bool aboveAvailable)
  {
    var l = H264BlockOrder.NeighbourLeft[block];
    var t = H264BlockOrder.NeighbourAbove[block];

    if ((l >= H264BlockOrder.Outside && !leftAvailable)
        || (t >= H264BlockOrder.Outside && !aboveAvailable))
      return 2;

    var left = l < H264BlockOrder.Outside ? modes[l] : leftModes[l - H264BlockOrder.Outside];
    var above = t < H264BlockOrder.Outside ? modes[t] : aboveModes[t - H264BlockOrder.Outside];
    return Math.Min(left, above);
  }

  private static int ReadChromaPredMode(
    CabacEngine cabac, in H264Neighbour left, in H264Neighbour above)
  {
    var ctxInc = (left.Available && left.ChromaPredModeNonZero && !left.Pcm ? 1 : 0)
               + (above.Available && above.ChromaPredModeNonZero && !above.Pcm ? 1 : 0);

    if (cabac.DecodeDecision(CtxIntraChromaPredMode + ctxInc) == 0)
      return 0;

    if (cabac.DecodeDecision(CtxIntraChromaPredMode + 3) == 0)
      return 1;

    return cabac.DecodeDecision(CtxIntraChromaPredMode + 3) == 0 ? 2 : 3;
  }

  private static (int Luma, int Chroma) ReadCodedBlockPattern(
    CabacEngine cabac, in H264Neighbour left, in H264Neighbour above)
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

  /// <summary>
  /// The 8x8 block to the left of (or above) block i is inside this macroblock for the right and
  /// bottom halves, so partially decoded bits of the current pattern feed their own contexts. The
  /// term is set when the neighbour holds no coefficients, not when it holds them.
  /// </summary>
  private static int LumaCondTerm(
    int block, int direction, int currentLuma,
    in H264Neighbour left, in H264Neighbour above)
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

  /// <summary>
  /// The first continuation bin takes its own context and every bin after it shares a third. One
  /// context for all of them decodes the same until a delta reaches two, then diverges silently.
  /// The context the leading bin takes turns on whether the previous macroblock's delta was
  /// non-zero, not on whether it was present.
  /// </summary>
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

/// <summary>
/// luma4x4BlkIdx is Morton order: 8x8 quadrants in raster order, 4x4 blocks in raster order within
/// each. Both directions are wanted - coding order to walk the blocks, raster to find a neighbour.
/// </summary>
internal static class H264BlockOrder
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

  /// <summary>
  /// Where each block's two neighbours are. Under <see cref="Outside"/> the neighbour belongs to
  /// this macroblock and the value is its coding index; at or above, it is in the macroblock to the
  /// left or above and the remainder indexes the edge run kept from it.
  /// </summary>
  public const int Outside = 16;

  public static readonly byte[] NeighbourLeft = Neighbour(left: true, morton: false);
  public static readonly byte[] NeighbourAbove = Neighbour(left: false, morton: false);

  /// <summary>
  /// The same for coded block flags, which a neighbour keeps in coding order rather than by edge.
  /// </summary>
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
