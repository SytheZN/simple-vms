using System.Buffers.Binary;
using System.Numerics;
using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

/// <summary>
/// A window onto the slice that reads zeros past its end rather than failing. Every CAVLC symbol
/// is found by peeking a fixed width and then consuming only what the table says was really
/// there, so a block near the end of a slice routinely looks further ahead than what remains.
/// </summary>
internal struct H264BitWindow(byte[] data, int length, int bitOffset)
{
  private readonly byte[] _data = data;
  private readonly int _length = length;
  private int _at = bitOffset;

  public readonly int BitPosition => _at;

  public readonly bool Exhausted => _at >= _length << 3;

  public void Skip(int bits) => _at += bits;

  public readonly uint Peek(int count)
  {
    var at = _at >> 3;
    var window = at + sizeof(ulong) <= _length
      ? BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(at))
      : Tail(at);

    return (uint)((window << (_at & 7)) >> (64 - count));
  }

  private readonly ulong Tail(int at)
  {
    ulong window = 0;
    for (var i = 0; i < sizeof(ulong); i++)
      window = (window << 8) | (at + i < _length ? _data[at + i] : 0ul);

    return window;
  }

  public uint Read(int count)
  {
    var value = Peek(count);
    _at += count;
    return value;
  }

  public bool ReadFlag() => Read(1) != 0;

  /// <summary>Leading zeros plus the one that ends them, which is what every prefix costs.</summary>
  public readonly int PrefixLength()
  {
    var value = Peek(32);
    return value == 0 ? 33 : BitOperations.LeadingZeroCount(value) + 1;
  }

  public uint ReadExpGolomb()
  {
    var prefix = PrefixLength();
    _at += prefix;
    var zeros = prefix - 1;
    return zeros == 0 ? 0 : (1u << zeros) - 1 + Read(zeros);
  }

  public int ReadSignedExpGolomb()
  {
    var value = ReadExpGolomb();
    if (value == 0) return 0;
    return (value & 1) == 1 ? (int)((value + 1) >> 1) : -(int)((value + 1) >> 1);
  }

  public void AlignToByte() => _at = (_at + 7) & ~7;
}

/// <summary>
/// The other entropy coder. Where CABAC spends a context per bin, CAVLC spends a variable-length
/// code per symbol and picks which code table to read it from by how many coefficients the
/// neighbouring blocks turned out to carry - which is why the counts travel with the neighbour
/// state rather than the coded-block flags CABAC needs.
/// </summary>
internal sealed class H264CavlcReader(
  byte[] rbsp, int length, int bitOffset, IReconstructionObserver? observer)
{
  private H264BitWindow _bits = new(rbsp, length, bitOffset);
  private readonly IReconstructionObserver? _observer = observer;

  /// <summary>Written before they are read on every path that reaches them.</summary>
  private readonly int[] _values = new int[16];
  private readonly int[] _runs = new int[16];

  /// <summary>Longest run of leading zeros a level prefix is allowed before it is nonsense.</summary>
  private const int MaxLevelPrefix = 15;

  public int BitPosition => _bits.BitPosition;

  public bool Exhausted => _bits.Exhausted;

  public void AlignToByte() => _bits.AlignToByte();

  public void SkipBytes(int count) => _bits.Skip(count << 3);

  public H264Macroblock ReadHeader(
    bool transform8x8Allowed, Span<sbyte> modes,
    ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes,
    bool leftAvailable, bool aboveAvailable)
  {
    var mbType = _bits.ReadExpGolomb();

    if (mbType == 25)
      return new H264Macroblock { Kind = H264MbKind.Pcm };

    if (mbType > 0)
    {
      var index = (int)(mbType - 1);
      return new H264Macroblock
      {
        Kind = H264MbKind.Intra16x16,
        CbpLuma = index >= 12 ? 15 : 0,
        CbpChroma = (index >> 2) % 3,
        Predicted16x16Mode = index & 3,
        ChromaPredMode = (int)_bits.ReadExpGolomb(),
      };
    }

    var transform8x8 = transform8x8Allowed && _bits.ReadFlag();
    ReadPredModes(
      modes, leftModes, aboveModes, leftAvailable, aboveAvailable, transform8x8 ? 4 : 1);

    var chromaMode = (int)_bits.ReadExpGolomb();
    var pattern = H264CavlcTables.Intra4x4CbpTable[_bits.ReadExpGolomb()];

    return new H264Macroblock
    {
      Kind = transform8x8 ? H264MbKind.Intra8x8 : H264MbKind.Intra4x4,
      CbpLuma = pattern & 15,
      CbpChroma = pattern >> 4,
      ChromaPredMode = chromaMode,
      Transform8x8 = transform8x8,
    };
  }

  public int ReadQpDelta() => _bits.ReadSignedExpGolomb();

  private void ReadPredModes(
    Span<sbyte> modes, ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes,
    bool leftAvailable, bool aboveAvailable, int span)
  {
    for (var i = 0; i < 16; i += span)
    {
      var predicted = H264MacroblockReader.PredictedMode(
        i, modes, leftModes, aboveModes, leftAvailable, aboveAvailable);

      var mode = predicted;
      if (!_bits.ReadFlag())
      {
        var remainder = (int)_bits.Read(3);
        mode = (sbyte)(remainder < predicted ? remainder : remainder + 1);
      }

      for (var j = 0; j < span; j++)
        modes[i + j] = mode;
    }
  }

  /// <summary>
  /// One residual block. Returns how many coefficients it carried, which is also what a later
  /// block's neighbour count wants, and fills the same sparse pair of arrays the CABAC reader
  /// does - positions ascending along the scan.
  /// </summary>
  public int ReadBlock(
    int neighbourCount, bool chromaDirect, int maxCoeff,
    ReadOnlySpan<byte> scan, Span<ushort> occupied, Span<int> levels)
  {
    _observer?.Begin(ReconstructionPhase.Significance);
    var (total, trailingOnes) = ReadCoeffToken(neighbourCount, chromaDirect);
    _observer?.End(ReconstructionPhase.Significance);

    if (total == 0) return 0;

    Span<int> values = _values;
    Span<int> runs = _runs;

    _observer?.Begin(ReconstructionPhase.Levels);
    ReadLevels(values, total, trailingOnes);
    _observer?.End(ReconstructionPhase.Levels);

    _observer?.Begin(ReconstructionPhase.Last);
    var zerosLeft = total < maxCoeff ? ReadTotalZeros(total, chromaDirect) : 0;
    if (zerosLeft < 0 || zerosLeft + total > maxCoeff)
    {
      _observer?.End(ReconstructionPhase.Last);
      return 0;
    }

    ReadRuns(runs, total, zerosLeft);
    _observer?.End(ReconstructionPhase.Last);

    _observer?.Begin(ReconstructionPhase.Emit);
    var position = -1;
    for (var i = total - 1; i >= 0; i--)
    {
      position += runs[i] + 1;
      occupied[total - 1 - i] = scan[position];
      levels[total - 1 - i] = values[i];
    }

    _observer?.End(ReconstructionPhase.Emit);
    return total;
  }

  /// <summary>
  /// Which of the code tables a block is read from turns on its neighbours' counts, and the widest
  /// of them is escaped through a fixed six-bit code instead. The narrow tables read eight bits
  /// first and only then learn how many more the symbol needs.
  /// </summary>
  private (int Total, int TrailingOnes) ReadCoeffToken(int neighbourCount, bool chromaDirect)
  {
    int symbol;

    if (chromaDirect)
    {
      var value = _bits.Peek(8);
      var entry = H264CavlcTables.CoeffTokenChromaDc[value];
      _bits.Skip(entry.Length);
      symbol = entry.Symbol;
    }
    else
    {
      var table = H264CavlcTables.NcMap[Math.Clamp(neighbourCount, 0, 16)];
      if (table > 2)
      {
        symbol = H264CavlcTables.CoeffTokenFixed[_bits.Read(6)].Symbol;
      }
      else
      {
        var value = _bits.Peek(8);
        if (value < H264CavlcTables.CoeffTokenMoreBitsThreshold[table])
        {
          _bits.Skip(8);
          var width = H264CavlcTables.CoeffTokenMoreBitsCount[table][value];
          var entry = H264CavlcTables.CoeffTokenSub[table][value][_bits.Peek(width)];
          _bits.Skip(entry.Length);
          symbol = entry.Symbol;
        }
        else
        {
          var entry = table switch
          {
            0 => H264CavlcTables.CoeffTokenPrimary0[value],
            1 => H264CavlcTables.CoeffTokenPrimary1[value],
            _ => H264CavlcTables.CoeffTokenPrimary2[value],
          };

          _bits.Skip(entry.Length);
          symbol = entry.Symbol;
        }
      }
    }

    var (total, trailingOnes) = H264CavlcTables.SymbolToCoeff(symbol);
    return total < 0 || trailingOnes > 3 || total > 16 ? (0, 0) : (total, trailingOnes);
  }

  /// <summary>
  /// Levels run from the highest frequency down. The suffix widens as soon as one of them exceeds
  /// what the current width can hold, so a block that starts small and grows costs less than one
  /// coded at a fixed width would.
  /// </summary>
  private void ReadLevels(Span<int> values, int total, int trailingOnes)
  {
    for (var i = 0; i < trailingOnes; i++)
      values[i] = _bits.ReadFlag() ? -1 : 1;

    var suffixLength = total > 10 && trailingOnes < 3 ? 1 : 0;

    for (var i = trailingOnes; i < total; i++)
    {
      var prefix = _bits.PrefixLength();
      if (prefix > MaxLevelPrefix + 1) prefix = MaxLevelPrefix + 1;
      _bits.Skip(prefix);

      var levelPrefix = prefix - 1;
      var code = levelPrefix << suffixLength;
      var suffixSize = suffixLength;

      if (levelPrefix == 14 && suffixLength == 0)
      {
        suffixSize = 4;
      }
      else if (levelPrefix >= 15)
      {
        suffixSize = 12;
        if (suffixLength == 0) code += 15;
      }

      if (suffixSize > 0)
        code += (int)_bits.Read(suffixSize);

      if (i == trailingOnes && trailingOnes < 3)
        code += 2;

      values[i] = (code & 1) == 0 ? (code + 2) >> 1 : -((code + 2) >> 1);

      if (suffixLength == 0) suffixLength = 1;

      var threshold = 3 << (suffixLength - 1);
      if (suffixLength < 6 && (values[i] > threshold || values[i] < -threshold))
        suffixLength++;
    }
  }

  private int ReadTotalZeros(int total, bool chromaDirect)
  {
    if (chromaDirect)
    {
      var width = H264CavlcTables.TotalZerosChromaDcBitWidths[total - 1];
      var entry = H264CavlcTables.TotalZerosChromaDc[total - 1][_bits.Peek(width)];
      _bits.Skip(entry.Length);
      return entry.Zeros;
    }

    var lumaWidth = H264CavlcTables.TotalZeros4x4BitWidths[total - 1];
    var lumaEntry = H264CavlcTables.TotalZeros4x4[total - 1][_bits.Peek(lumaWidth)];
    _bits.Skip(lumaEntry.Length);
    return lumaEntry.Zeros;
  }

  /// <summary>
  /// The last coefficient takes whatever zeros are left over, so only the ones before it are
  /// coded - and once none are left the rest follow one another with no gap at all.
  /// </summary>
  private void ReadRuns(Span<int> runs, int total, int zerosLeft)
  {
    for (var i = 0; i < total - 1; i++)
    {
      if (zerosLeft <= 0)
      {
        for (var j = i; j < total; j++)
          runs[j] = 0;

        return;
      }

      var width = H264CavlcTables.RunBeforeBitWidths[Math.Min(zerosLeft, 7) - 1];
      var value = _bits.Peek(width);

      if (zerosLeft < 7)
      {
        var entry = H264CavlcTables.RunBefore[zerosLeft - 1][value];
        _bits.Skip(entry.Length);
        runs[i] = entry.Run;
      }
      else
      {
        _bits.Skip(width);
        var entry = H264CavlcTables.RunBefore[6][value];
        if (entry.Run < 7)
        {
          runs[i] = entry.Run;
        }
        else
        {
          var prefix = _bits.PrefixLength();
          runs[i] = prefix + 6;
          _bits.Skip(prefix);
        }
      }

      zerosLeft -= runs[i];
    }

    runs[total - 1] = zerosLeft < 0 ? 0 : zerosLeft;
  }
}
