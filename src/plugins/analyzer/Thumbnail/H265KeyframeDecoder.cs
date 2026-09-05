using H265;
using Microsoft.Extensions.Logging;
using Utils;

namespace Analyzer.Thumbnail;

internal sealed class H265KeyframeDecoder
{
  private const int CtxSplitCu = 2;
  private const int CtxCuTransquantBypass = 5;
  private const int CtxPartMode = 11;
  private const int CtxPrevIntraLumaPred = 15;
  private const int CtxIntraChromaPredMode = 16;
  private const int CtxSplitTransform = 126;
  private const int CtxCbfLuma = 129;
  private const int CtxCbfCbCr = 131;
  private const int CtxSaoMergeFlag = 0;
  private const int CtxSaoTypeIdx = 1;
  private const int CtxCuQpDeltaAbs = 150;

  private readonly ILogger _logger;
  private IObserverHarness<ReconstructionPhase>? _observer;
  private readonly Dictionary<uint, H265Sps> _spsById = [];
  private readonly Dictionary<uint, H265Pps> _ppsById = [];
  private string? _lastRejection;

  private H265Sps _sps = null!;
  private H265Pps _pps = null!;
  private readonly CabacEngine _cabac = new();
  private readonly ResidualReader _reader = new();
  private byte[] _rbsp = [];
  private int _rbspLength;
  private bool _configured;
  private int _configuredBound;
  private sealed class Plane
  {
    public byte[] Band = [];
    public byte[] Output = [];
    public int BandWidth;
    public int BandHeight;
    public int BandTop;
    public int OutputWidth;
    public int Width;
    public int Height;
    public int Shift;
    public int DecodedShift;
    public H265IntraPrediction.Neighbourhood View;
  }

  private readonly Plane _lumaPlane = new();
  private readonly Plane _cbPlane = new();
  private readonly Plane _crPlane = new();

  private int _codedWidth;
  private int _codedHeight;
  private byte[] _decoded = [];
  private readonly byte[] _references = new byte[4 * 32 + 1];
  private readonly byte[] _crReferences = new byte[4 * 32 + 1];
  private readonly int[] _main = new int[3 * 32 + 2];
  private readonly int[] _cellSums = new int[32];
  private readonly ushort[] _occupied = new ushort[32 * 32];
  private readonly int[] _levels = new int[32 * 32];
  private readonly bool[] _prevFlags = new bool[4];
  private readonly int[] _mpmIdx = new int[4];
  private readonly int[] _edgeWork = new int[128];
  private readonly byte[] _predictedBottom = new byte[32];
  private readonly byte[] _predictedRight = new byte[32];
  private readonly byte[] _predictedCells = new byte[32 * 32];
  private readonly int[] _coefficients = new int[32 * 32];
  private readonly int[] _residual = new int[32 * 32];
  private readonly int[] _bottomRow = new int[32];
  private readonly int[] _rightColumn = new int[32];
  private readonly int[] _cells = new int[32 * 32];
  private int _qp;
  private bool _cuQpDeltaCoded;
  private int _chromaMode;
  private bool _transquantBypass;
  private int _minQp;
  private int _maxQp;
  private byte[] _lumaModes = [];
  private byte[] _ctDepth = [];
  private byte[] _qpMap = [];
  private int _qpPrev;
  private int _modeStride;

  private int _log2CtbSize;
  private int _log2MinCbSize;
  private int _log2MinTbSize;
  private int _log2MaxTbSize;
  private int _maxTransformDepth;
  private int _qpGroupLog2;
  private bool _cuQpDeltaEnabled;
  private bool _strongSmoothing;
  private bool _transformSkipEnabled;
  private bool _signDataHiding;
  private bool _failed;
  private string _failReason = "";

  private void Fail(string reason)
  {
    if (_failed) return;
    _failed = true;
    _failReason = reason;
  }

  private void Fail(int x, int y, int log2TrSize, int cIdx) =>
    Fail($"implausible residual at ({x},{y}), {1 << log2TrSize}x{1 << log2TrSize} cIdx {cIdx}");

  private H265IntraPrediction.Workspace _workspace;

  private H265IntraPrediction.Workspace _crWorkspace;

  private H265InverseTransform.Workspace _transformWork;

  private void BuildWorkspaces()
  {
    _workspace = new H265IntraPrediction.Workspace
    {
      References = _references,
      Main = _main,
      Sums = _cellSums,
      Bottom = _predictedBottom,
      Right = _predictedRight,
      Means = _predictedCells,
      Observer = _observer,
    };
    _crWorkspace = new H265IntraPrediction.Workspace
    {
      References = _crReferences,
      Main = _main,
      Sums = _cellSums,
      Bottom = _predictedBottom,
      Right = _predictedRight,
      Means = _predictedCells,
      Observer = _observer,
    };
    _transformWork = new H265InverseTransform.Workspace
    {
      Block = _coefficients,
      Stage = _residual,
      EdgeStage = _edgeWork,
      Bottom = _bottomRow,
      Right = _rightColumn,
      Cells = _cells,
      Observer = _observer,
    };
  }

  public H265KeyframeDecoder(ILogger logger)
  {
    _logger = logger;
    BuildWorkspaces();
  }

  internal void Observe(IObserverHarness<ReconstructionPhase> observer)
  {
    _observer = observer;
    _reader.Observe(observer);
    BuildWorkspaces();
  }

  public void AddParameterSet(ReadOnlySpan<byte> nal, byte nalUnitType)
  {
    if (nalUnitType == 33)
    {
      var sps = H265Sps.Parse(nal);
      if (sps == null) return;
      _spsById[sps.Id] = sps;
    }
    else if (nalUnitType == 34)
    {
      var pps = H265Pps.Parse(nal);
      _ppsById[pps.Id] = pps;
    }
    else
    {
      return;
    }

    _configured = false;
  }

  private const int MaxShift = 2;

  private static void Setup(Plane plane, int width, int height, int ctbSize, int shift, int decodedShift)
  {
    plane.Width = width;
    plane.Height = height;
    plane.Shift = shift;
    plane.DecodedShift = decodedShift;
    plane.BandWidth = width;
    plane.BandHeight = ctbSize + 1;
    plane.BandTop = -1;
    plane.OutputWidth = width >> shift;

    plane.Band = new byte[plane.BandWidth * plane.BandHeight];
    plane.Output = new byte[plane.OutputWidth * (height >> shift)];
  }

  private void AdvanceBand(Plane plane, int top)
  {
    Array.Copy(plane.Band, (plane.BandHeight - 1) * plane.BandWidth, plane.Band, 0, plane.BandWidth);
    plane.BandTop = top - 1;

    plane.View = new H265IntraPrediction.Neighbourhood
    {
      Band = plane.Band,
      BandWidth = plane.BandWidth,
      BandTop = plane.BandTop,
      Width = plane.Width,
      Height = plane.Height,
      Decoded = _decoded,
      DecodedStride = _modeStride,
      DecodedShift = plane.DecodedShift,
    };
  }

  private static int ChooseShift(int width, int height, int boundingSize)
  {
    var longest = Math.Max(width, height);
    var shift = 0;
    while (shift < MaxShift && (longest >> (shift + 1)) >= boundingSize)
      shift++;
    return shift;
  }

  public DecodedFrame? Decode(ReadOnlySpan<byte> nal, byte nalUnitType, int boundingSize)
  {
    if (!Configure(boundingSize, out var reason))
      return Reject(reason);

    if (_rbsp.Length < nal.Length) _rbsp = new byte[nal.Length];
    _rbspLength = BitstreamHelpers.ExtractRbsp(nal, _rbsp);

    var header = H265SliceHeader.Parse(_rbsp, nalUnitType, _sps, _pps);
    if (header == null)
      return Reject($"Slice header parse failed for NAL type {nalUnitType} against PPS {_pps.Id}");

    return DecodeSlice(header);
  }

  private bool Configure(int boundingSize, out string reason)
  {
    if (_configured && boundingSize == _configuredBound)
    {
      reason = "";
      return true;
    }

    reason = _ppsById.Count == 0 || _spsById.Count == 0
      ? "Keyframe arrived before any SPS/PPS"
      : "No PPS resolves to a known SPS";

    foreach (var pps in _ppsById.Values)
    {
      if (!_spsById.TryGetValue(pps.SpsId, out var sps)) continue;

      if (sps.ChromaFormatIdc != 1)
      {
        reason = $"Chroma format {sps.ChromaFormatIdc} is not 4:2:0";
        continue;
      }

      if (sps.PcmEnabled)
      {
        reason = "PCM coding is enabled on this stream";
        continue;
      }

      _sps = sps;
      _pps = pps;
      _codedWidth = sps.CodedWidth;
      _codedHeight = sps.CodedHeight;

      _log2CtbSize = sps.Log2CtbSize;
      _log2MinCbSize = sps.Log2MinCbSize;
      _log2MinTbSize = sps.Log2MinTbSize;
      _log2MaxTbSize = sps.Log2MaxTbSize;
      _maxTransformDepth = sps.MaxTransformHierarchyDepthIntra;
      _qpGroupLog2 = sps.Log2CtbSize - pps.DiffCuQpDeltaDepth;
      _cuQpDeltaEnabled = pps.CuQpDeltaEnabled;
      _strongSmoothing = sps.StrongIntraSmoothing;
      _transformSkipEnabled = pps.TransformSkipEnabled;
      _signDataHiding = pps.SignDataHiding;

      var shift = ChooseShift(sps.Width, sps.Height, boundingSize);
      var ctbSize = 1 << sps.Log2CtbSize;

      Setup(_lumaPlane, _codedWidth, _codedHeight, ctbSize, shift, 2);
      Setup(_cbPlane, _codedWidth / 2, _codedHeight / 2, ctbSize / 2, shift, 1);
      Setup(_crPlane, _codedWidth / 2, _codedHeight / 2, ctbSize / 2, shift, 1);

      _modeStride = (_codedWidth >> 2) + 1;
      var cells = _modeStride * ((_codedHeight >> 2) + 1);
      _lumaModes = new byte[cells];
      _ctDepth = new byte[cells];
      _decoded = new byte[cells];
      _qpMap = new byte[cells];

      _configured = true;
      _configuredBound = boundingSize;
      reason = "";
      return true;
    }

    return false;
  }

  private DecodedFrame? Reject(string reason)
  {
    if (reason == _lastRejection) return null;
    _lastRejection = reason;
    _logger.LogWarning("Keyframe rejected: {Reason}", reason);
    return null;
  }

  private DecodedFrame? DecodeSlice(H265SliceHeader header)
  {
    var ctbSize = 1 << _sps.Log2CtbSize;

    Array.Fill(_lumaModes, (byte)1);
    Array.Clear(_ctDepth);
    Array.Clear(_decoded);
    Array.Fill(_qpMap, (byte)header.SliceQp);


    _cabac.Initialize(_rbsp, _rbspLength, header.BitOffset, header.SliceQp, H264.CabacInitType.I);
    _qp = header.SliceQp;
    _qpPrev = _qp;
    _minQp = _qp;
    _maxQp = _qp;
    _failed = false;
    _failReason = "";

    for (var y = 0; y < _sps.CodedHeight; y += ctbSize)
    {
      AdvanceBand(_lumaPlane, y);
      AdvanceBand(_cbPlane, y / 2);
      AdvanceBand(_crPlane, y / 2);

      for (var x = 0; x < _sps.CodedWidth; x += ctbSize)
      {
        if (_sps.SaoEnabled && (header.SaoLuma || header.SaoChroma))
          ReadSao(x, y, header);

        DecodeQuadtree(x, y, _sps.Log2CtbSize, 0);
        if (_failed) return Reject($"Desynchronised in CTB ({x},{y}): {_failReason}");

        if (_cabac.DecodeTerminate() == 1)
        {
          if (x + ctbSize >= _sps.CodedWidth && y + ctbSize >= _sps.CodedHeight)
            return Finish();

          return Reject(
            $"Slice ended at CTB {(y / ctbSize) * _sps.CtbWidth + (x / ctbSize)} " +
            $"of {_sps.CtbWidth * _sps.CtbHeight} ({x},{y}), " +
            $"{_cabac.BytesRead} of {_cabac.BytesTotal} bytes consumed, " +
            $"qp {_minQp}..{_maxQp} from {header.SliceQp}");
        }
      }
    }

    return Reject(
      $"Slice ran past its last CTB, {_cabac.BytesRead} of {_cabac.BytesTotal} bytes consumed");
  }

  private DecodedFrame Finish()
  {
    var width = _sps.Width >> _lumaPlane.Shift;
    var height = _sps.Height >> _lumaPlane.Shift;
    var chromaWidth = ((_sps.Width + 1) / 2) >> _cbPlane.Shift;
    var chromaHeight = ((_sps.Height + 1) / 2) >> _cbPlane.Shift;

    if (width == _lumaPlane.OutputWidth && chromaWidth == _cbPlane.OutputWidth
        && height * width == _lumaPlane.Output.Length)
      return new DecodedFrame(
        _lumaPlane.Output, _cbPlane.Output, _crPlane.Output,
        width, height, chromaWidth, chromaHeight);

    return new DecodedFrame(
      CropPlane(_lumaPlane.Output, ref _croppedLuma, _lumaPlane.OutputWidth, width, height),
      CropPlane(_cbPlane.Output, ref _croppedCb, _cbPlane.OutputWidth, chromaWidth, chromaHeight),
      CropPlane(_crPlane.Output, ref _croppedCr, _crPlane.OutputWidth, chromaWidth, chromaHeight),
      width, height, chromaWidth, chromaHeight);
  }

  private byte[] _croppedLuma = [];
  private byte[] _croppedCb = [];
  private byte[] _croppedCr = [];

  private static byte[] CropPlane(
    byte[] source, ref byte[] cropped, int sourceStride, int width, int height)
  {
    var size = width * height;
    if (cropped.Length != size) cropped = new byte[size];

    for (var y = 0; y < height; y++)
      Array.Copy(source, y * sourceStride, cropped, y * width, width);

    return cropped;
  }

  private void ReadSao(int x, int y, H265SliceHeader header)
  {
    if (x > 0 && _cabac.DecodeDecision(CtxSaoMergeFlag) == 1) return;
    if (y > 0 && _cabac.DecodeDecision(CtxSaoMergeFlag) == 1) return;

    Span<int> offsets = stackalloc int[4];
    var chromaTypeIdx = 0;

    for (var component = 0; component < 3; component++)
    {
      if (!(component == 0 ? header.SaoLuma : header.SaoChroma)) continue;

      int typeIdx;
      if (component == 2)
      {
        typeIdx = chromaTypeIdx;
      }
      else
      {
        typeIdx = ReadSaoTypeIdx();
        if (component == 1) chromaTypeIdx = typeIdx;
      }

      if (typeIdx == 0) continue;

      for (var i = 0; i < 4; i++)
        offsets[i] = ReadTruncatedRice(7);

      if (typeIdx == 1)
      {
        for (var i = 0; i < 4; i++)
          if (offsets[i] != 0)
            _cabac.DecodeBypass();
        _cabac.DecodeBypassBits(5);
      }
      else if (component < 2)
      {
        _cabac.DecodeBypassBits(2);
      }
    }
  }

  private int ReadSaoTypeIdx()
  {
    if (_cabac.DecodeDecision(CtxSaoTypeIdx) == 0) return 0;
    return _cabac.DecodeBypass() == 1 ? 2 : 1;
  }

  private int ReadTruncatedRice(int max) => _cabac.DecodeBypassUnary(max);

  private void DecodeQuadtree(int x, int y, int log2CbSize, int depth)
  {
    if (_failed) return;
    if (x >= _codedWidth || y >= _codedHeight) return;

    var split = log2CbSize > _log2MinCbSize;
    if (split && x + (1 << log2CbSize) <= _codedWidth && y + (1 << log2CbSize) <= _codedHeight)
      split = _cabac.DecodeDecision(CtxSplitCu + SplitContext(x, y, depth)) == 1;

    if (_cuQpDeltaEnabled
        && (log2CbSize == _qpGroupLog2 || (!split && log2CbSize > _qpGroupLog2)))
      StartQuantizationGroup(x, y);

    if (split)
    {
      var half = 1 << (log2CbSize - 1);
      DecodeQuadtree(x, y, log2CbSize - 1, depth + 1);
      DecodeQuadtree(x + half, y, log2CbSize - 1, depth + 1);
      DecodeQuadtree(x, y + half, log2CbSize - 1, depth + 1);
      DecodeQuadtree(x + half, y + half, log2CbSize - 1, depth + 1);
      return;
    }

    StoreDepth(x, y, 1 << log2CbSize, depth);
    DecodeCodingUnit(x, y, log2CbSize);
  }

  private int SplitContext(int x, int y, int depth)
  {
    var inc = 0;
    if (x > 0 && DepthAt(x - 1, y) > depth) inc++;
    if (y > 0 && DepthAt(x, y - 1) > depth) inc++;
    return inc;
  }

  private int MapIndex(int x, int y) => (y >> 2) * _modeStride + (x >> 2);

  private int DepthAt(int x, int y) => _ctDepth[MapIndex(x, y)];

  private void StartQuantizationGroup(int x, int y)
  {
    _cuQpDeltaCoded = false;
    _qpPrev = _qp;

    var ctb = ~((1 << _log2CtbSize) - 1);
    var left = x > 0 && ((x - 1) & ctb) == (x & ctb) ? QpAt(x - 1, y) : _qpPrev;
    var above = y > 0 && ((y - 1) & ctb) == (y & ctb) ? QpAt(x, y - 1) : _qpPrev;

    _qp = (left + above + 1) >> 1;
  }

  private int QpAt(int x, int y) => _qpMap[MapIndex(x, y)];

  private void Store(byte[] map, int x, int y, int size, byte value)
  {
    var entries = size >> 2;
    var column = x >> 2;
    var first = MapIndex(column << 2, y);

    if (entries == 1)
    {
      if (first < map.Length) map[first] = value;
      return;
    }

    var rows = entries;
    for (var row = first; rows > 0 && row < map.Length; row += _modeStride, rows--)
      map.AsSpan(row, Math.Min(entries, map.Length - row)).Fill(value);
  }

  private void StoreQp(int x, int y, int size) => Store(_qpMap, x, y, size, (byte)_qp);

  private void StoreDepth(int x, int y, int size, int depth) =>
    Store(_ctDepth, x, y, size, (byte)depth);

  private void DecodeCodingUnit(int x, int y, int log2CbSize)
  {
    _observer?.Begin(ReconstructionPhase.Header);

    _transquantBypass = _pps.TransquantBypassEnabled
      && _cabac.DecodeDecision(CtxCuTransquantBypass) == 1;

    var partNxN = false;
    if (log2CbSize == _log2MinCbSize && log2CbSize > 2)
      partNxN = _cabac.DecodeDecision(CtxPartMode) == 0;

    if (!partNxN)
    {
      var only = _cabac.DecodeDecision(CtxPrevIntraLumaPred) == 1;
      var index = only ? ReadTruncatedRiceBypass(2) : (int)_cabac.DecodeBypassBits(5);
      var lumaMode = DeriveLumaMode(x, y, only, index);

      StoreMode(x, y, 1 << log2CbSize, lumaMode);
      _chromaMode = ReadChromaMode(lumaMode);

      _observer?.End(ReconstructionPhase.Header);

      DecodeTransformTree(x, y, x, y, log2CbSize, 0, 0, false, true, true);
      StoreQp(x, y, 1 << log2CbSize);
      return;
    }

    const int partShift = 1;
    const int parts = 1 << partShift;
    var partSize = 1 << (log2CbSize - partShift);

    Span<bool> prevFlags = _prevFlags;
    Span<int> mpmIdx = _mpmIdx;

    const int count = parts * parts;
    for (var i = 0; i < count; i++)
      prevFlags[i] = _cabac.DecodeDecision(CtxPrevIntraLumaPred) == 1;

    for (var i = 0; i < count; i++)
      mpmIdx[i] = prevFlags[i]
        ? ReadTruncatedRiceBypass(2)
        : (int)_cabac.DecodeBypassBits(5);

    var firstLumaMode = 1;
    for (var i = 0; i < count; i++)
    {
      var px = x + (i & (parts - 1)) * partSize;
      var py = y + (i >> partShift) * partSize;
      var mode = DeriveLumaMode(px, py, prevFlags[i], mpmIdx[i]);
      if (i == 0) firstLumaMode = mode;
      StoreMode(px, py, partSize, mode);
    }

    _chromaMode = ReadChromaMode(firstLumaMode);

    _observer?.End(ReconstructionPhase.Header);

    DecodeTransformTree(x, y, x, y, log2CbSize, 0, 0, partNxN, true, true);
    StoreQp(x, y, 1 << log2CbSize);
  }

  private int ReadTruncatedRiceBypass(int max) => _cabac.DecodeBypassUnary(max);

  private static readonly int[] ChromaModeCandidates = [0, 26, 10, 1];

  private int ReadChromaMode(int lumaMode)
  {
    if (_cabac.DecodeDecision(CtxIntraChromaPredMode) == 0)
      return lumaMode;

    var mode = ChromaModeCandidates[(int)_cabac.DecodeBypassBits(2)];
    return mode == lumaMode ? 34 : mode;
  }

  private int DeriveLumaMode(int x, int y, bool prevFlag, int idx)
  {
    var candA = ModeAt(x - 1, y);
    var candB = y > 0 && (y & ((1 << _log2CtbSize) - 1)) != 0 ? ModeAt(x, y - 1) : 1;

    int first, second, third;
    if (candA == candB)
    {
      if (candA < 2)
      {
        first = 0;
        second = 1;
        third = 26;
      }
      else
      {
        first = candA;
        second = 2 + ((candA + 29) & 31);
        third = 2 + ((candA - 1) & 31);
      }
    }
    else
    {
      first = candA;
      second = candB;
      third = candA != 0 && candB != 0 ? 0 : candA != 1 && candB != 1 ? 1 : 26;
    }

    if (prevFlag)
      return idx == 0 ? first : idx == 1 ? second : third;

    var low = Math.Min(first, second);
    var high = Math.Max(first, second);
    first = Math.Min(low, third);
    var middle = Math.Max(low, third);
    second = Math.Min(high, middle);
    third = Math.Max(high, middle);

    var mode = idx;
    mode += mode >= first ? 1 : 0;
    mode += mode >= second ? 1 : 0;
    mode += mode >= third ? 1 : 0;
    return mode;
  }

  private int ModeAt(int x, int y)
  {
    if (x < 0 || y < 0) return 1;
    return _lumaModes[MapIndex(x, y)];
  }

  private void StoreMode(int x, int y, int size, int mode) =>
    Store(_lumaModes, x, y, size, (byte)mode);

  private void DecodeTransformTree(
    int x, int y, int xBase, int yBase, int log2TrSize, int depth, int blkIdx,
    bool intraSplit, bool parentCbfCb, bool parentCbfCr)
  {
    if (_failed) return;

    _observer?.Begin(ReconstructionPhase.Header);

    var split = false;
    if (log2TrSize <= _log2MaxTbSize && log2TrSize > _log2MinTbSize
        && depth < _maxTransformDepth && !(intraSplit && depth == 0))
      split = _cabac.DecodeDecision(CtxSplitTransform + 5 - log2TrSize) == 1;
    else
      split = log2TrSize > _log2MaxTbSize || (intraSplit && depth == 0);

    var cbfCb = parentCbfCb;
    var cbfCr = parentCbfCr;
    if (log2TrSize > 2)
    {
      if (parentCbfCb)
        cbfCb = _cabac.DecodeDecision(CtxCbfCbCr + depth) == 1;
      if (parentCbfCr)
        cbfCr = _cabac.DecodeDecision(CtxCbfCbCr + depth) == 1;
    }

    if (split)
    {
      _observer?.End(ReconstructionPhase.Header);

      var half = 1 << (log2TrSize - 1);
      DecodeTransformTree(x, y, x, y, log2TrSize - 1, depth + 1, 0, intraSplit, cbfCb, cbfCr);
      DecodeTransformTree(x + half, y, x, y, log2TrSize - 1, depth + 1, 1, intraSplit, cbfCb, cbfCr);
      DecodeTransformTree(x, y + half, x, y, log2TrSize - 1, depth + 1, 2, intraSplit, cbfCb, cbfCr);
      DecodeTransformTree(x + half, y + half, x, y, log2TrSize - 1, depth + 1, 3, intraSplit, cbfCb, cbfCr);
      return;
    }

    var cbfLuma = _cabac.DecodeDecision(CtxCbfLuma + (depth == 0 ? 1 : 0)) == 1;

    _observer?.End(ReconstructionPhase.Header);

    DecodeTransformUnit(x, y, xBase, yBase, log2TrSize, blkIdx, cbfLuma, cbfCb, cbfCr);
  }

  private void DecodeTransformUnit(
    int x, int y, int xBase, int yBase, int log2TrSize, int blkIdx,
    bool cbfLuma, bool cbfCb, bool cbfCr)
  {
    var chromaHere = log2TrSize > 2 || blkIdx == 3;

    if ((cbfLuma || cbfCb || cbfCr) && _cuQpDeltaEnabled && !_cuQpDeltaCoded)
    {
      _observer?.Begin(ReconstructionPhase.Header);
      ReadQpDelta();
      _cuQpDeltaCoded = true;
      _observer?.End(ReconstructionPhase.Header);
      if (_failed) return;
    }

    _observer?.Block(cbfLuma);

    H265IntraPrediction.Reference(
      in _lumaPlane.View, in _workspace, x, y, 1 << log2TrSize, null, default);

    if (!Reconstruct(_lumaPlane, in _workspace, x, y, log2TrSize, 0, ModeAt(x, y), cbfLuma))
      return;

    MarkDecoded(x, y, 1 << log2TrSize);

    if (!chromaHere) return;

    var chromaLog2 = log2TrSize > 2 ? log2TrSize - 1 : log2TrSize;
    var cx = (log2TrSize > 2 ? x : xBase) / 2;
    var cy = (log2TrSize > 2 ? y : yBase) / 2;

    _observer?.Block(cbfCb);

    H265IntraPrediction.Reference(
      in _cbPlane.View, in _workspace, cx, cy, 1 << chromaLog2, _crPlane.Band, _crReferences);

    if (!Reconstruct(_cbPlane, in _workspace, cx, cy, chromaLog2, 1, _chromaMode, cbfCb))
      return;

    _observer?.Block(cbfCr);
    Reconstruct(_crPlane, in _crWorkspace, cx, cy, chromaLog2, 2, _chromaMode, cbfCr);
  }

  private bool Reconstruct(
    Plane plane, in H265IntraPrediction.Workspace work, int x, int y, int log2TrSize, int cIdx,
    int mode, bool coded)
  {
    var size = 1 << log2TrSize;
    var isLuma = cIdx == 0;
    var log2Out = log2TrSize - plane.Shift;
    var cells = 1 << log2Out;

    H265IntraPrediction.Predict(in work, size, cells, mode, isLuma, _strongSmoothing);

    var residual = coded;
    if (coded)
    {
      var scanIdx = ScanFor(mode, log2TrSize, cIdx);

      var read = _reader.Read(_cabac, log2TrSize, cIdx, scanIdx,
        _transformSkipEnabled && !_transquantBypass,
        _signDataHiding && !_transquantBypass,
        _occupied, _levels, out var levelCount, out var transformSkip);

      if (!read)
      {
        Fail(x, y, log2TrSize, cIdx);
        return false;
      }

      if (_transquantBypass)
      {
        _observer?.Begin(ReconstructionPhase.Samples);
        H265InverseTransform.Spread(
          in _transformWork, _occupied.AsSpan(0, levelCount), _levels.AsSpan(0, levelCount), size);
        H265InverseTransform.Split(in _transformWork, size, cells);
        _observer?.End(ReconstructionPhase.Samples);
      }
      else
      {
        var qp = cIdx switch
        {
          0 => _qp,
          1 => ChromaQp(_pps.CbQpOffset),
          _ => ChromaQp(_pps.CrQpOffset),
        };

        H265InverseTransform.Apply(
          in _transformWork, log2TrSize, log2Out, qp, transformSkip, isLuma && log2TrSize == 2,
          _occupied.AsSpan(0, levelCount), _levels.AsSpan(0, levelCount));
      }
    }

    _observer?.Begin(ReconstructionPhase.Write);
    WriteEdges(plane, x, y, size, residual);
    WriteCells(plane, x, y, cells, residual);
    _observer?.End(ReconstructionPhase.Write);
    return true;
  }

  private void WriteEdges(Plane plane, int x, int y, int size, bool residual)
  {
    var band = plane.Band;
    var width = plane.BandWidth;
    var last = size - 1;
    var bottom = (y + last - plane.BandTop) * width + x;
    var right = (y - plane.BandTop) * width + x + last;

    if (!residual)
    {
      for (var i = 0; i < size; i++)
        band[bottom + i] = _predictedBottom[i];

      for (var i = 0; i < size; i++, right += width)
        band[right] = _predictedRight[i];

      return;
    }

    for (var i = 0; i < size; i++)
      band[bottom + i] = Combine(_predictedBottom[i], _bottomRow[i]);

    for (var i = 0; i < size; i++, right += width)
      band[right] = Combine(_predictedRight[i], _rightColumn[i]);
  }

  private void WriteCells(Plane plane, int x, int y, int cells, bool residual)
  {
    var output = plane.Output;
    var width = plane.OutputWidth;
    var at = (y >> plane.Shift) * width + (x >> plane.Shift);

    if (cells == 1)
    {
      output[at] = residual ? Combine(_predictedCells[0], _cells[0]) : _predictedCells[0];
      return;
    }

    var source = 0;

    if (!residual)
    {
      for (var cy = 0; cy < cells; cy++, at += width, source += cells)
        for (var cx = 0; cx < cells; cx++)
          output[at + cx] = _predictedCells[source + cx];

      return;
    }

    for (var cy = 0; cy < cells; cy++, at += width)
      for (var cx = 0; cx < cells; cx++, source++)
        output[at + cx] = Combine(_predictedCells[source], _cells[source]);
  }

  private static byte Combine(byte prediction, int residual) =>
    (byte)Math.Clamp(prediction + residual, 0, 255);

  private void MarkDecoded(int x, int y, int size) => Store(_decoded, x, y, size, 1);

  private static readonly int[] ChromaQpFrom30 = [29, 30, 31, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37];

  private int ChromaQp(int offset)
  {
    var qpi = Math.Clamp(_qp + offset, 0, 51);
    if (qpi < 30) return qpi;
    return qpi > 42 ? qpi - 6 : ChromaQpFrom30[qpi - 30];
  }

  private void ReadQpDelta()
  {
    var magnitude = 0;
    while (magnitude < 5 && _cabac.DecodeDecision(CtxCuQpDeltaAbs + (magnitude > 0 ? 1 : 0)) == 1)
      magnitude++;

    if (magnitude == 5)
      magnitude += (int)ReadExpGolombBypass();

    if (magnitude == 0) return;

    if (magnitude > 26)
    {
      Fail($"cu_qp_delta_abs {magnitude} exceeds the legal range");
      return;
    }

    var delta = _cabac.DecodeBypass() == 1 ? -magnitude : magnitude;

    _qp = ((_qp + delta) % 52 + 52) % 52;
    _minQp = Math.Min(_minQp, _qp);
    _maxQp = Math.Max(_maxQp, _qp);
  }

  private uint ReadExpGolombBypass()
  {
    var k = _cabac.DecodeBypassUnary(31);
    return k == 0 ? 0 : _cabac.DecodeBypassBits(k) + (1u << k) - 1;
  }

  private static ScanIdx ScanFor(int mode, int log2TrSize, int cIdx)
  {
    if (log2TrSize != 2 && !(log2TrSize == 3 && cIdx == 0))
      return ScanIdx.Diagonal;
    if (mode is >= 6 and <= 14) return ScanIdx.Vertical;
    if (mode is >= 22 and <= 30) return ScanIdx.Horizontal;
    return ScanIdx.Diagonal;
  }

}
