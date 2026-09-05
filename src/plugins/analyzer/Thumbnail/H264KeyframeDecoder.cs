using H264;
using Microsoft.Extensions.Logging;
using Utils;

namespace Analyzer.Thumbnail;

internal sealed record DecodedFrame(
  byte[] Luma, byte[] Cb, byte[] Cr,
  int LumaWidth, int LumaHeight, int ChromaWidth, int ChromaHeight)
{
  public static DecodedFrame Subsampled(
    byte[] luma, byte[] cb, byte[] cr, int width, int height) =>
    new(luma, cb, cr, width, height, (width + 1) / 2, (height + 1) / 2);
}

internal sealed class H264KeyframeDecoder
{
  private const int Shift = 2;

  private const int MacroblockSize = 16;

  private static readonly byte[] RightColumn = Edge(row: false);
  private static readonly byte[] BottomRow = Edge(row: true);

  private static byte[] Edge(bool row)
  {
    var blocks = new byte[4];
    for (var i = 0; i < blocks.Length; i++)
      blocks[i] = BlockOrder.Index[row ? 3 * 4 + i : i * 4 + 3];

    return blocks;
  }

  private const int Outside = BlockOrder.Outside;

  private sealed class Plane
  {
    public byte[] Band = [];
    public byte[] Output = [];
    public int BandWidth;
    public int BandHeight;
    public int BandTop;
    public int OutputWidth;
    public H264Neighbourhood View;
  }

  private readonly ILogger _logger;
  private readonly Dictionary<uint, H264Sps> _spsById = [];
  private readonly Dictionary<uint, H264Pps> _ppsById = [];
  private string? _lastRejection;

  private H264Dequant? _dequant;
  private H264Sps? _dequantSps;
  private H264Pps? _dequantPps;

  private readonly Plane _lumaPlane = new();
  private readonly Plane _cbPlane = new();
  private readonly Plane _crPlane = new();

  private byte[] _croppedLuma = [];
  private byte[] _croppedCb = [];
  private byte[] _croppedCr = [];

  private readonly byte[] _references = new byte[H264Workspace.Length(MacroblockSize)];
  private readonly byte[] _crReferences = new byte[H264Workspace.Length(MacroblockSize)];
  private readonly byte[] _predictedBottom = new byte[MacroblockSize];
  private readonly byte[] _predictedRight = new byte[MacroblockSize];
  private readonly byte[] _predictedCells = new byte[MacroblockSize];
  private readonly byte[] _crBottom = new byte[MacroblockSize];
  private readonly byte[] _crRight = new byte[MacroblockSize];
  private readonly byte[] _crCells = new byte[MacroblockSize];

  private readonly int[] _residualBottom = new int[8];
  private readonly int[] _residualRight = new int[8];
  private readonly int[] _residualCells = new int[4];

  private readonly ushort[] _occupied = new ushort[64];
  private readonly int[] _levels = new int[64];
  private readonly int[] _directTerms = new int[16];
  private readonly int[] _cbDirect = new int[4];
  private readonly int[] _crDirect = new int[4];

  private readonly sbyte[] _modes = new sbyte[16];
  private readonly sbyte[] _leftModes = new sbyte[4];
  private sbyte[] _aboveModes = [];
  private Neighbour[] _above = [];

  private H264CavlcReader? _cavlc;

  private readonly sbyte[] _counts = new sbyte[16];
  private readonly sbyte[] _cbCounts = new sbyte[4];
  private readonly sbyte[] _crCounts = new sbyte[4];
  private readonly sbyte[] _leftCounts = new sbyte[4];
  private readonly sbyte[] _leftCbCounts = new sbyte[2];
  private readonly sbyte[] _leftCrCounts = new sbyte[2];
  private sbyte[] _aboveCounts = [];
  private sbyte[] _aboveCbCounts = [];
  private sbyte[] _aboveCrCounts = [];

  private const sbyte NoNeighbour = -1;

  private ushort _leftCbf;
  private ushort _aboveCbf;

  private readonly CabacEngine _cabac = new();
  private readonly ResidualReader _residual = new();
  private byte[] _rbsp = [];
  private int _rbspLength;
  private IObserverHarness<ReconstructionPhase>? _observer;

  private H264Workspace _work;
  private H264Workspace _crWork;
  private H264InverseTransform.Workspace _transformWork;

  public H264KeyframeDecoder(ILogger logger)
  {
    _logger = logger;
    _transformWork = new H264InverseTransform.Workspace
    {
      Bottom = _residualBottom,
      Right = _residualRight,
      Cells = _residualCells,
    };
  }

  internal void Observe(IObserverHarness<ReconstructionPhase> observer)
  {
    _observer = observer;
    _residual.Observe(observer);
    _transformWork = _transformWork with { Observer = observer };
  }

  public void AddParameterSet(ReadOnlySpan<byte> nal, byte nalUnitType)
  {
    if (nalUnitType == 7)
    {
      var sps = H264Sps.Parse(nal);
      _spsById[sps.Id] = sps;
    }
    else if (nalUnitType == 8)
    {
      var pps = H264Pps.Parse(nal);
      _ppsById[pps.Id] = pps;
    }
  }

  public DecodedFrame? Decode(ReadOnlySpan<byte> nal, byte nalUnitType, byte nalRefIdc)
  {
    if (_ppsById.Count == 0 || _spsById.Count == 0)
      return Reject("Keyframe arrived before any SPS/PPS");

    if (_rbsp.Length < nal.Length) _rbsp = new byte[nal.Length];
    _rbspLength = BitstreamHelpers.ExtractRbsp(nal, _rbsp);
    var rbsp = _rbsp.AsSpan(0, _rbspLength);

    var probePps = _ppsById.Values.First();
    var probeSps = _spsById.Values.First();
    var header = H264SliceHeader.Parse(rbsp, nalUnitType, nalRefIdc, probeSps, probePps);
    if (header == null)
      return Reject($"Slice header parse failed for NAL type {nalUnitType}");

    if (!_ppsById.TryGetValue(header.PpsId, out var pps))
      return Reject($"Slice references unknown PPS {header.PpsId}");
    if (!_spsById.TryGetValue(pps.SpsId, out var sps))
      return Reject($"PPS {pps.Id} references unknown SPS {pps.SpsId}");

    if (!ReferenceEquals(pps, probePps) || !ReferenceEquals(sps, probeSps))
    {
      header = H264SliceHeader.Parse(rbsp, nalUnitType, nalRefIdc, sps, pps);
      if (header == null)
        return Reject($"Slice header parse failed against PPS {pps.Id}");
    }

    if (!pps.CabacEnabled && pps.Transform8x8Mode)
      return Reject("CAVLC streams using the 8x8 transform are not supported");
    if (sps.ChromaFormatIdc != 1)
      return Reject($"Chroma format {sps.ChromaFormatIdc} is not 4:2:0");
    if (!sps.FrameMbsOnly)
      return Reject("Field and MBAFF coding are not supported");

    return DecodeSlice(header, sps, pps);
  }

  private DecodedFrame? Reject(string reason)
  {
    if (reason == _lastRejection) return null;
    _lastRejection = reason;
    _logger.LogWarning("Keyframe rejected: {Reason}", reason);
    return null;
  }

  private static void Setup(Plane plane, int width, int height, int blockSize)
  {
    plane.BandWidth = width;
    plane.BandHeight = blockSize + 1;
    plane.BandTop = -1;
    plane.OutputWidth = width >> Shift;

    var band = plane.BandWidth * plane.BandHeight;
    if (plane.Band.Length != band) plane.Band = new byte[band];

    var output = plane.OutputWidth * (height >> Shift);
    if (plane.Output.Length != output) plane.Output = new byte[output];
  }

  private static void AdvanceBand(Plane plane, int top)
  {
    Array.Copy(plane.Band, (plane.BandHeight - 1) * plane.BandWidth, plane.Band, 0, plane.BandWidth);
    plane.BandTop = top - 1;

    plane.View = new H264Neighbourhood
    {
      Band = plane.Band,
      BandWidth = plane.BandWidth,
      BandTop = plane.BandTop,
    };
  }

  private DecodedFrame? DecodeSlice(H264SliceHeader header, H264Sps sps, H264Pps pps)
  {
    if (_dequant == null || !ReferenceEquals(sps, _dequantSps) || !ReferenceEquals(pps, _dequantPps))
    {
      _dequant = H264Dequant.Build(sps.ScalingMatrix, pps.ScalingMatrix);
      _dequantSps = sps;
      _dequantPps = pps;
    }

    var mbWidth = sps.WidthInMbs;
    var mbHeight = sps.HeightInMbs;

    Setup(_lumaPlane, mbWidth * 16, mbHeight * 16, 16);
    Setup(_cbPlane, mbWidth * 8, mbHeight * 8, 8);
    Setup(_crPlane, mbWidth * 8, mbHeight * 8, 8);

    if (_above.Length != mbWidth) _above = new Neighbour[mbWidth];
    else Array.Clear(_above);

    if (_aboveModes.Length != mbWidth * 4) _aboveModes = new sbyte[mbWidth * 4];
    Array.Fill(_aboveModes, (sbyte)2);

    if (_aboveCounts.Length != mbWidth * 4) _aboveCounts = new sbyte[mbWidth * 4];
    Array.Fill(_aboveCounts, NoNeighbour);

    if (_aboveCbCounts.Length != mbWidth * 2)
    {
      _aboveCbCounts = new sbyte[mbWidth * 2];
      _aboveCrCounts = new sbyte[mbWidth * 2];
    }

    Array.Fill(_aboveCbCounts, NoNeighbour);
    Array.Fill(_aboveCrCounts, NoNeighbour);
    Array.Fill(_leftCounts, NoNeighbour);
    Array.Fill(_leftCbCounts, NoNeighbour);
    Array.Fill(_leftCrCounts, NoNeighbour);

    _work = new H264Workspace
    {
      References = _references,
      Bottom = _predictedBottom,
      Right = _predictedRight,
      Means = _predictedCells,
      Observer = _observer,
    };

    _crWork = new H264Workspace
    {
      References = _crReferences,
      Bottom = _crBottom,
      Right = _crRight,
      Means = _crCells,
      Observer = _observer,
    };

    if (pps.CabacEnabled)
    {
      _cavlc = null;
      _cabac.Initialize(_rbsp, _rbspLength, header.BitOffset, header.SliceQp, CabacInitType.I);
    }
    else
    {
      _cavlc = new H264CavlcReader(_rbsp, _rbspLength, header.BitOffset, _observer);
    }

    var left = default(Neighbour);
    var qp = header.SliceQp;
    var previousDelta = 0;

    var aboveLeft = false;

    var counted = _cavlc != null;

    var mbX = (int)header.FirstMbInSlice % mbWidth;
    var mbY = (int)header.FirstMbInSlice / mbWidth;

    for (var mbAddr = (int)header.FirstMbInSlice; mbAddr < mbWidth * mbHeight; mbAddr++)
    {
      if (mbX == 0)
      {
        left = default;
        aboveLeft = false;
        Array.Fill(_leftModes, (sbyte)2);

        if (counted)
        {
          Array.Fill(_leftCounts, NoNeighbour);
          Array.Fill(_leftCbCounts, NoNeighbour);
          Array.Fill(_leftCrCounts, NoNeighbour);
        }

        AdvanceBand(_lumaPlane, mbY * 16);
        AdvanceBand(_cbPlane, mbY * 8);
        AdvanceBand(_crPlane, mbY * 8);
      }

      if (counted)
      {
        Array.Clear(_counts);
        Array.Clear(_cbCounts);
        Array.Clear(_crCounts);
      }

      var aboveState = _above[mbX];
      var aboveModes = _aboveModes.AsSpan(mbX * 4, 4);

      var mask = H264Availability.Mask(
        left.Available, aboveState.Available,
        aboveLeft, mbX + 1 < mbWidth && _above[mbX + 1].Available);

      _leftCbf = ResolvedCbf(left);
      _aboveCbf = ResolvedCbf(aboveState);

      _observer?.Begin(ReconstructionPhase.Header);

      var mb = _cavlc == null
        ? MacroblockReader.ReadHeader(
          _cabac, pps.Transform8x8Mode, left, aboveState, _modes, _leftModes, aboveModes)
        : _cavlc.ReadHeader(
          pps.Transform8x8Mode, _modes, _leftModes, aboveModes,
          left.Available, aboveState.Available);

      if (!mb.IsNxN)
        Array.Fill(_modes, (sbyte)2);

      _observer?.End(ReconstructionPhase.Header);

      Neighbour state;

      if (mb.Kind == MbKind.Pcm)
      {
        if (!ReconstructPcm(mbX, mbY))
          return Reject("Raw sample macroblock runs past the end of the slice");

        state = new Neighbour { Available = true, Pcm = true, CbpLuma = 15, CbpChroma = 2 };
        previousDelta = 0;

        if (counted)
        {
          Array.Fill(_counts, (sbyte)16);
          Array.Fill(_cbCounts, (sbyte)16);
          Array.Fill(_crCounts, (sbyte)16);
        }

        _observer?.Block(false);
      }
      else
      {
        state = new Neighbour
        {
          Available = true,
          IsNxN = mb.IsNxN,
          CbpLuma = mb.CbpLuma,
          CbpChroma = mb.CbpChroma,
          ChromaPredModeNonZero = mb.ChromaPredMode != 0,
          Transform8x8 = mb.Transform8x8,
        };

        if (mb.CbpLuma != 0 || mb.CbpChroma != 0 || mb.Kind == MbKind.Intra16x16)
        {
          previousDelta = _cavlc == null
            ? MacroblockReader.ReadQpDelta(_cabac, previousDelta)
            : _cavlc.ReadQpDelta();

          qp += previousDelta;
          if (qp < 0) qp += 52;
          else if (qp >= 52) qp -= 52;
        }
        else
        {
          previousDelta = 0;
        }

        _observer?.Block(mb.CbpLuma != 0);

        if (mb.Kind == MbKind.Intra16x16)
          ReconstructWhole(mbX, mbY, qp, mask, mb, ref state, left, aboveState);
        else if (mb.Kind == MbKind.Intra8x8)
          ReconstructBlocks8x8(mbX, mbY, qp, mask, mb, ref state);
        else
          ReconstructBlocks(mbX, mbY, qp, mask, mb, ref state);

        ReconstructChroma(mbX, mbY, qp, mask, pps, mb, ref state, left, aboveState);
      }

      for (var i = 0; i < 4; i++)
      {
        _leftModes[i] = _modes[RightColumn[i]];
        _aboveModes[mbX * 4 + i] = _modes[BottomRow[i]];
      }

      if (counted)
      {
        for (var i = 0; i < 4; i++)
        {
          _leftCounts[i] = _counts[RightColumn[i]];
          _aboveCounts[mbX * 4 + i] = _counts[BottomRow[i]];
        }

        for (var i = 0; i < 2; i++)
        {
          _leftCbCounts[i] = _cbCounts[i * 2 + 1];
          _leftCrCounts[i] = _crCounts[i * 2 + 1];
          _aboveCbCounts[mbX * 2 + i] = _cbCounts[2 + i];
          _aboveCrCounts[mbX * 2 + i] = _crCounts[2 + i];
        }
      }

      left = state;
      aboveLeft = aboveState.Available;
      _above[mbX] = state;

      if (++mbX == mbWidth)
      {
        mbX = 0;
        mbY++;
      }

      if (_cavlc == null ? _cabac.DecodeTerminate() == 1 : _cavlc.Exhausted)
        break;
    }

    return Finish(sps);
  }

  private void ReconstructBlocks(
    int mbX, int mbY, int qp, int mask, in Macroblock mb, ref Neighbour state)
  {
    var plane = _lumaPlane;
    var dequant = _dequant!.Luma4x4[qp];

    for (var i = 0; i < 16; i++)
    {
      var (bx, by) = BlockOrder.Position[i];
      var x = mbX * 16 + bx * 4;
      var y = mbY * 16 + by * 4;

      var found = H264Availability.Of(H264Availability.Blocks4x4, i, mask);
      H264IntraPrediction.Reference(in plane.View, in _work, x, y, 4, found);
      H264IntraPrediction.Predict4x4(in _work, _modes[i], found);

      var count = (mb.CbpLuma & (1 << (i >> 2))) != 0
        ? ReadLumaBlock(i, mbX, ref state)
        : 0;

      if (count > 0)
        H264InverseTransform.Combine4x4(
          in _work, _occupied.AsSpan(0, count), _levels.AsSpan(0, count), dequant);

      _observer?.Begin(ReconstructionPhase.Write);
      Write(plane, in _work, x, y, 4, 1);
      _observer?.End(ReconstructionPhase.Write);
    }
  }

  private void ReconstructBlocks8x8(
    int mbX, int mbY, int qp, int mask, in Macroblock mb, ref Neighbour state)
  {
    var plane = _lumaPlane;
    var dequant = _dequant!.Luma8x8[qp];

    for (var i = 0; i < 4; i++)
    {
      var x = mbX * 16 + (i & 1) * 8;
      var y = mbY * 16 + (i >> 1) * 8;

      var found = H264Availability.Of(H264Availability.Blocks8x8, i, mask);
      H264IntraPrediction.Reference(in plane.View, in _work, x, y, 8, found);

      H264IntraPrediction.Predict8x8(in _work, _modes[i * 4], found);

      if ((mb.CbpLuma & (1 << i)) != 0)
      {
        var count = _residual.Read8x8(
          _cabac, H264.ResidualTables.Zigzag8x8, _occupied, _levels);

        if (count > 0)
        {
          state.LumaCbf |= (ushort)(0xF << (i * 4));

          H264InverseTransform.Apply8x8(
            in _transformWork, _occupied.AsSpan(0, count), _levels.AsSpan(0, count), qp, dequant);

          Correct8x8(in _work);
        }
      }

      _observer?.Begin(ReconstructionPhase.Write);
      Write(plane, in _work, x, y, 8, 2);
      _observer?.End(ReconstructionPhase.Write);
    }
  }

  private void Correct8x8(in H264Workspace work)
  {
    for (var k = 0; k < 8; k++)
    {
      work.Bottom[k] = H264Workspace.Combine(work.Bottom[k], _residualBottom[k]);
      work.Right[k] = H264Workspace.Combine(work.Right[k], _residualRight[k]);
    }

    for (var k = 0; k < 4; k++)
      work.Means[k] = H264Workspace.Combine(work.Means[k], _residualCells[k]);
  }

  private void ReconstructWhole(
    int mbX, int mbY, int qp, int mask, in Macroblock mb,
    ref Neighbour state, in Neighbour left, in Neighbour above)
  {
    var plane = _lumaPlane;
    var x = mbX * 16;
    var y = mbY * 16;

    var found = H264Availability.Of(H264Availability.Whole, 0, mask);
    H264IntraPrediction.Reference(in plane.View, in _work, x, y, 16, found);
    H264IntraPrediction.Predict16x16(in _work, mb.Predicted16x16Mode, found);

    var dequant = _dequant!.Luma4x4[qp];
    Array.Clear(_directTerms);

    var direct = ReadLumaDirect(mbX, left, above);

    if (direct > 0)
    {
      state.DcCbf = true;
      ReadDirectTerms(direct, dequant[0]);
    }

    for (var i = 0; i < 16; i++)
    {
      var (bx, by) = BlockOrder.Position[i];
      var count = (mb.CbpLuma & (1 << (i >> 2))) != 0
        ? ReadLumaAlternating(i, mbX, ref state)
        : 0;

      if (count == 0 && _directTerms[i] == 0) continue;

      H264InverseTransform.Apply4x4(
        in _transformWork, _occupied.AsSpan(0, count), _levels.AsSpan(0, count),
        _directTerms[i], dequant);

      Correct(in _work, bx, by, 16, 4);
    }

    _observer?.Begin(ReconstructionPhase.Write);
    Write(plane, in _work, x, y, 16, 4);
    _observer?.End(ReconstructionPhase.Write);
  }

  private void ReconstructChroma(
    int mbX, int mbY, int qp, int mask, H264Pps pps, in Macroblock mb,
    ref Neighbour state, in Neighbour left, in Neighbour above)
  {
    var x = mbX * 8;
    var y = mbY * 8;

    var found = H264Availability.Of(H264Availability.Whole, 0, mask);
    H264IntraPrediction.ReferencePair(
      in _cbPlane.View, in _work, x, y, 8, found, _crPlane.Band, _crReferences);

    H264IntraPrediction.PredictChroma(in _work, mb.ChromaPredMode, found);
    H264IntraPrediction.PredictChroma(in _crWork, mb.ChromaPredMode, found);

    var cbDequant = _dequant!.Cb4x4[ChromaQp(qp, pps.ChromaQpIndexOffset)];
    var crDequant = _dequant.Cr4x4[ChromaQp(qp, pps.SecondChromaQpIndexOffset)];

    Array.Clear(_cbDirect);
    Array.Clear(_crDirect);

    if (mb.CbpChroma != 0)
    {
      state.CbDcCbf = ReadChromaDirect(_cbDirect, cbDequant,
        DirectCodedBlockFlag(left, left.CbDcCbf), DirectCodedBlockFlag(above, above.CbDcCbf));

      state.CrDcCbf = ReadChromaDirect(_crDirect, crDequant,
        DirectCodedBlockFlag(left, left.CrDcCbf), DirectCodedBlockFlag(above, above.CrDcCbf));
    }

    state.CbCbf = ReconstructComponent(
      _cbPlane, in _work, _cbDirect, cbDequant, mb.CbpChroma == 2,
      left.CbCbf, above.CbCbf, left, above, mbX, x, y,
      _cbCounts, _leftCbCounts, _aboveCbCounts);

    state.CrCbf = ReconstructComponent(
      _crPlane, in _crWork, _crDirect, crDequant, mb.CbpChroma == 2,
      left.CrCbf, above.CrCbf, left, above, mbX, x, y,
      _crCounts, _leftCrCounts, _aboveCrCounts);
  }

  private byte ReconstructComponent(
    Plane plane, in H264Workspace work, int[] direct, int[] dequant, bool alternating,
    byte leftMask, byte aboveMask, in Neighbour left, in Neighbour above,
    int mbX, int x, int y, sbyte[] counts, sbyte[] leftCounts, sbyte[] aboveCounts)
  {
    byte mask = 0;

    for (var i = 0; i < 4; i++)
    {
      var bx = i & 1;
      var by = i >> 1;
      var count = 0;

      if (alternating)
      {
        if (_cavlc == null)
        {
          var condA = bx > 0 ? (mask >> (i - 1)) & 1
            : !left.Available || left.Pcm ? 1 : (leftMask >> (i + 1)) & 1;
          var condB = by > 0 ? (mask >> (i - 2)) & 1
            : !above.Available || above.Pcm ? 1 : (aboveMask >> (i + 2)) & 1;

          count = _residual.Read(
            _cabac, ResidualCategory.ChromaAlternating, condA, condB,
            H264.ResidualTables.Zigzag4x4.AsSpan(1), _occupied, _levels);
        }
        else
        {
          count = _cavlc.ReadBlock(
            ChromaNeighbourCount(i, mbX, counts, leftCounts, aboveCounts),
            false, 15, H264.ResidualTables.Zigzag4x4.AsSpan(1), _occupied, _levels);
        }

        counts[i] = (sbyte)count;
        if (count > 0)
          mask |= (byte)(1 << i);
      }

      if (count == 0 && direct[i] == 0) continue;

      H264InverseTransform.Apply4x4(
        in _transformWork, _occupied.AsSpan(0, count), _levels.AsSpan(0, count),
        direct[i], dequant);

      Correct(in work, bx, by, 8, 2);
    }

    _observer?.Begin(ReconstructionPhase.Write);
    Write(plane, in work, x, y, 8, 2);
    _observer?.End(ReconstructionPhase.Write);
    return mask;
  }

  private bool ReadChromaDirect(int[] target, int[] dequant, int condA, int condB)
  {
    var count = _cavlc == null
      ? _residual.Read(
        _cabac, ResidualCategory.ChromaDirect, condA, condB,
        H264.ResidualTables.ChromaDirectScan, _occupied, _levels)
      : _cavlc.ReadBlock(
        0, true, 4, H264.ResidualTables.ChromaDirectScan, _occupied, _levels);

    if (count == 0) return false;

    Span<int> block = stackalloc int[4];
    for (var i = 0; i < count; i++)
      block[_occupied[i]] = _levels[i];

    H264InverseTransform.ChromaDirect(block, dequant[0]);
    block.CopyTo(target);
    return true;
  }

  private void ReadDirectTerms(int count, int scale)
  {
    Span<int> block = stackalloc int[16];
    for (var i = 0; i < count; i++)
    {
      var (bx, by) = BlockOrder.Position[_occupied[i]];
      block[by * 4 + bx] = _levels[i];
    }

    H264InverseTransform.LumaDirect(block, scale);

    for (var i = 0; i < 16; i++)
    {
      var (bx, by) = BlockOrder.Position[i];
      _directTerms[i] = block[by * 4 + bx];
    }
  }

  private void Correct(in H264Workspace work, int bx, int by, int span, int cells)
  {
    var at = by * cells + bx;
    work.Means[at] = H264Workspace.Combine(work.Means[at], _residualCells[0]);

    if (by * 4 + 3 == span - 1)
    {
      var bottom = work.Bottom;
      for (var k = 0; k < 4; k++)
        bottom[bx * 4 + k] = H264Workspace.Combine(bottom[bx * 4 + k], _residualBottom[k]);
    }

    if (bx * 4 + 3 == span - 1)
    {
      var right = work.Right;
      for (var k = 0; k < 4; k++)
        right[by * 4 + k] = H264Workspace.Combine(right[by * 4 + k], _residualRight[k]);
    }
  }

  private static void Write(Plane plane, in H264Workspace work, int x, int y, int size, int cells)
  {
    var band = plane.Band;
    var bandWidth = plane.BandWidth;
    var last = size - 1;
    var bottom = (y + last - plane.BandTop) * bandWidth + x;
    var right = (y - plane.BandTop) * bandWidth + x + last;

    var edges = work.Bottom;
    for (var i = 0; i < size; i++)
      band[bottom + i] = edges[i];

    edges = work.Right;
    for (var i = 0; i < size; i++, right += bandWidth)
      band[right] = edges[i];

    var output = plane.Output;
    var outputWidth = plane.OutputWidth;
    var at = (y >> Shift) * outputWidth + (x >> Shift);
    var means = work.Means;

    if (cells == 1)
    {
      output[at] = means[0];
      return;
    }

    var source = 0;
    for (var cy = 0; cy < cells; cy++, at += outputWidth)
      for (var cx = 0; cx < cells; cx++, source++)
        output[at + cx] = means[source];
  }

  private int ReadLumaBlock(int block, int mbX, ref Neighbour state)
  {
    int count;
    if (_cavlc == null)
    {
      var (condA, condB) = LumaCodedBlockFlag(block, state.LumaCbf);
      count = _residual.Read(
        _cabac, ResidualCategory.Luma, condA, condB,
        H264.ResidualTables.Zigzag4x4, _occupied, _levels);
    }
    else
    {
      count = _cavlc.ReadBlock(
        LumaNeighbourCount(block, mbX), false, 16,
        H264.ResidualTables.Zigzag4x4, _occupied, _levels);
    }

    _counts[block] = (sbyte)count;
    if (count > 0) state.LumaCbf |= (ushort)(1 << block);
    return count;
  }

  private int ReadLumaAlternating(int block, int mbX, ref Neighbour state)
  {
    int count;
    if (_cavlc == null)
    {
      var (condA, condB) = LumaCodedBlockFlag(block, state.LumaCbf);
      count = _residual.Read(
        _cabac, ResidualCategory.LumaAlternating, condA, condB,
        H264.ResidualTables.Zigzag4x4.AsSpan(1), _occupied, _levels);
    }
    else
    {
      count = _cavlc.ReadBlock(
        LumaNeighbourCount(block, mbX), false, 15,
        H264.ResidualTables.Zigzag4x4.AsSpan(1), _occupied, _levels);
    }

    _counts[block] = (sbyte)count;
    if (count > 0) state.LumaCbf |= (ushort)(1 << block);
    return count;
  }

  private int ReadLumaDirect(int mbX, in Neighbour left, in Neighbour above) =>
    _cavlc == null
      ? _residual.Read(
        _cabac, ResidualCategory.LumaDirect,
        DirectCodedBlockFlag(left, left.DcCbf), DirectCodedBlockFlag(above, above.DcCbf),
        H264.ResidualTables.LumaDirectScan, _occupied, _levels)
      : _cavlc.ReadBlock(
        LumaNeighbourCount(0, mbX), false, 16,
        H264.ResidualTables.LumaDirectScan, _occupied, _levels);

  private int LumaNeighbourCount(int block, int mbX)
  {
    var l = BlockOrder.NeighbourLeft[block];
    var t = BlockOrder.NeighbourAbove[block];

    int a = l < Outside ? _counts[l] : _leftCounts[l - Outside];
    int b = t < Outside ? _counts[t] : _aboveCounts[mbX * 4 + t - Outside];

    return NeighbourAverage(a, b);
  }

  private static int ChromaNeighbourCount(
    int block, int mbX, sbyte[] counts, sbyte[] leftCounts, sbyte[] aboveCounts)
  {
    var bx = block & 1;
    var by = block >> 1;

    int a = bx > 0 ? counts[block - 1] : leftCounts[by];
    int b = by > 0 ? counts[block - 2] : aboveCounts[mbX * 2 + bx];

    return NeighbourAverage(a, b);
  }

  private static int NeighbourAverage(int a, int b)
  {
    if (a < 0 && b < 0) return 0;
    if (a < 0) return b;
    if (b < 0) return a;
    return (a + b + 1) >> 1;
  }

  private static int DirectCodedBlockFlag(in Neighbour neighbour, bool coded) =>
    !neighbour.Available || neighbour.Pcm || coded ? 1 : 0;

  private (int A, int B) LumaCodedBlockFlag(int block, ushort current)
  {
    var l = BlockOrder.CbfLeft[block];
    var t = BlockOrder.CbfAbove[block];

    var a = ((l < Outside ? current : _leftCbf) >> (l & (Outside - 1))) & 1;
    var b = ((t < Outside ? current : _aboveCbf) >> (t & (Outside - 1))) & 1;

    return (a, b);
  }

  private static ushort ResolvedCbf(in Neighbour neighbour) =>
    !neighbour.Available || neighbour.Pcm ? (ushort)0xFFFF : neighbour.LumaCbf;

  private static int ChromaQp(int lumaQp, int offset) =>
    H264.ResidualTables.ChromaQp[Math.Clamp(lumaQp + offset, 0, 51)];

  private bool ReconstructPcm(int mbX, int mbY)
  {
    int at;
    if (_cavlc == null)
    {
      at = _cabac.Suspend();
    }
    else
    {
      _cavlc.AlignToByte();
      at = _cavlc.BitPosition >> 3;
    }

    if (at + 384 > _rbspLength) return false;

    FillPcm(_lumaPlane, in _work, mbX * 16, mbY * 16, 16, 4, at);
    FillPcm(_cbPlane, in _work, mbX * 8, mbY * 8, 8, 2, at + 256);
    FillPcm(_crPlane, in _crWork, mbX * 8, mbY * 8, 8, 2, at + 320);

    if (_cavlc == null)
      _cabac.Resume(at + 384);
    else
      _cavlc.SkipBytes(384);

    return true;
  }

  private void FillPcm(
    Plane plane, in H264Workspace work, int x, int y, int size, int cells, int at)
  {
    for (var i = 0; i < size; i++)
    {
      work.Bottom[i] = _rbsp[at + (size - 1) * size + i];
      work.Right[i] = _rbsp[at + i * size + size - 1];
    }

    var group = size / cells;
    for (var cy = 0; cy < cells; cy++)
      for (var cx = 0; cx < cells; cx++)
      {
        var total = 0;
        for (var sy = 0; sy < group; sy++)
          for (var sx = 0; sx < group; sx++)
            total += _rbsp[at + (cy * group + sy) * size + cx * group + sx];

        work.Means[cy * cells + cx] = (byte)(total / (group * group));
      }

    _observer?.Begin(ReconstructionPhase.Write);
    Write(plane, in work, x, y, size, cells);
    _observer?.End(ReconstructionPhase.Write);
  }

  private DecodedFrame? Finish(H264Sps sps)
  {
    var width = sps.CroppedWidth >> Shift;
    var height = sps.CroppedHeight >> Shift;
    var chromaWidth = (sps.CroppedWidth / 2) >> Shift;
    var chromaHeight = (sps.CroppedHeight / 2) >> Shift;

    return new DecodedFrame(
      Crop(_lumaPlane, ref _croppedLuma, width, height),
      Crop(_cbPlane, ref _croppedCb, chromaWidth, chromaHeight),
      Crop(_crPlane, ref _croppedCr, chromaWidth, chromaHeight),
      width, height, chromaWidth, chromaHeight);
  }

  private static byte[] Crop(Plane plane, ref byte[] cropped, int width, int height)
  {
    if (width == plane.OutputWidth && height * width == plane.Output.Length)
      return plane.Output;

    var size = width * height;
    if (cropped.Length != size) cropped = new byte[size];

    for (var y = 0; y < height; y++)
      Array.Copy(plane.Output, y * plane.OutputWidth, cropped, y * width, width);

    return cropped;
  }
}
