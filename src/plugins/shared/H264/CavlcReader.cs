using System.Buffers.Binary;
using System.Numerics;
using Utils;

namespace H264;

internal struct BitWindow(byte[] data, int length, int bitOffset)
{
  private readonly byte[] _data = data;
  private readonly int _length = length;
  private int _at = bitOffset;
  private ulong _window;
  private int _cached;

  public readonly int BitPosition => _at;

  public readonly bool Exhausted => _at >= _length << 3;

  public void Skip(int bits)
  {
    _at += bits;
    if (bits < _cached)
    {
      _window <<= bits;
      _cached -= bits;
    }
    else
    {
      _cached = 0;
    }
  }

  private void Ensure(int count)
  {
    if (_cached >= count) return;

    var at = _at >> 3;
    _window = (at + sizeof(ulong) <= _length
      ? BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(at))
      : Tail(at)) << (_at & 7);
    _cached = 64 - (_at & 7);
  }

  private readonly ulong Tail(int at)
  {
    ulong window = 0;
    for (var i = 0; i < sizeof(ulong); i++)
      window = (window << 8) | (at + i < _length ? _data[at + i] : 0ul);

    return window;
  }

  public uint Peek(int count)
  {
    Ensure(count);
    return (uint)(_window >> (64 - count));
  }

  public uint Read(int count)
  {
    Ensure(count);
    var value = (uint)(_window >> (64 - count));
    _at += count;
    _window <<= count;
    _cached -= count;
    return value;
  }

  public bool ReadFlag() => Read(1) != 0;

  public int PrefixLength()
  {
    var value = Peek(32);
    return value == 0 ? 33 : BitOperations.LeadingZeroCount(value) + 1;
  }

  public uint ReadExpGolomb()
  {
    var prefix = PrefixLength();
    Skip(prefix);
    var zeros = prefix - 1;
    return zeros == 0 ? 0 : (1u << zeros) - 1 + Read(zeros);
  }

  public int ReadSignedExpGolomb()
  {
    var value = ReadExpGolomb();
    if (value == 0) return 0;
    return (value & 1) == 1 ? (int)((value + 1) >> 1) : -(int)((value + 1) >> 1);
  }

  public void AlignToByte() => Skip((-_at) & 7);
}

public class CavlcReader(
  byte[] rbsp, int length, int bitOffset, IObserverHarness<ReconstructionPhase>? observer)
{
  private BitWindow _bits = new(rbsp, length, bitOffset);
  private readonly IObserverHarness<ReconstructionPhase>? _observer = observer;

  private readonly int[] _values = new int[16];
  private readonly int[] _runs = new int[16];

  private const int MaxLevelPrefix = 15;

  public int BitPosition => _bits.BitPosition;

  public bool Exhausted => _bits.Exhausted;

  public void AlignToByte() => _bits.AlignToByte();

  public void SkipBytes(int count) => _bits.Skip(count << 3);

  public void Skip(int bits) => _bits.Skip(bits);

  public uint Read(int count) => _bits.Read(count);

  public bool ReadFlag() => _bits.ReadFlag();

  public uint ReadExpGolomb() => _bits.ReadExpGolomb();

  public int ReadSignedExpGolomb() => _bits.ReadSignedExpGolomb();

  public int ReadQpDelta() => _bits.ReadSignedExpGolomb();

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

  public (int Total, int Activity) WalkBlock(int neighbourCount, bool chromaDirect, int maxCoeff)
  {
    _observer?.Begin(ReconstructionPhase.Significance);
    var (total, trailingOnes) = ReadCoeffToken(neighbourCount, chromaDirect);
    _observer?.End(ReconstructionPhase.Significance);

    if (total == 0) return (0, 0);

    _observer?.Begin(ReconstructionPhase.Levels);
    var activity = ReadLevels(_values, total, trailingOnes);
    _observer?.End(ReconstructionPhase.Levels);

    _observer?.Begin(ReconstructionPhase.Last);
    var zerosLeft = total < maxCoeff ? ReadTotalZeros(total, chromaDirect) : 0;
    if (zerosLeft < 0 || zerosLeft + total > maxCoeff)
    {
      _observer?.End(ReconstructionPhase.Last);
      return (-1, 0);
    }

    SkipRuns(total, zerosLeft);
    _observer?.End(ReconstructionPhase.Last);

    var lowFreq = Math.Clamp(4 - zerosLeft, 0, total);
    return (total, ActivityWeighting.Weigh(activity - total, lowFreq, total - lowFreq));
  }

  private (int Total, int TrailingOnes) ReadCoeffToken(int neighbourCount, bool chromaDirect)
  {
    int symbol;

    if (chromaDirect)
    {
      var value = _bits.Peek(8);
      var entry = CavlcTables.CoeffTokenChromaDc[value];
      _bits.Skip(entry.Length);
      symbol = entry.Symbol;
    }
    else
    {
      var table = CavlcTables.NcMap[Math.Clamp(neighbourCount, 0, 16)];
      if (table > 2)
      {
        symbol = CavlcTables.CoeffTokenFixed[_bits.Read(6)].Symbol;
      }
      else
      {
        var value = _bits.Peek(8);
        if (value < CavlcTables.CoeffTokenMoreBitsThreshold[table])
        {
          _bits.Skip(8);
          var width = CavlcTables.CoeffTokenMoreBitsCount[table][value];
          var entry = CavlcTables.CoeffTokenSub[table][value][_bits.Peek(width)];
          _bits.Skip(entry.Length);
          symbol = entry.Symbol;
        }
        else
        {
          var entry = table switch
          {
            0 => CavlcTables.CoeffTokenPrimary0[value],
            1 => CavlcTables.CoeffTokenPrimary1[value],
            _ => CavlcTables.CoeffTokenPrimary2[value],
          };

          _bits.Skip(entry.Length);
          symbol = entry.Symbol;
        }
      }
    }

    var (total, trailingOnes) = CavlcTables.SymbolToCoeff(symbol);
    return total < 0 || trailingOnes > 3 || total > 16 ? (0, 0) : (total, trailingOnes);
  }

  private int ReadLevels(Span<int> values, int total, int trailingOnes)
  {
    for (var i = 0; i < trailingOnes; i++)
      values[i] = _bits.ReadFlag() ? -1 : 1;

    var activity = trailingOnes;
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

      var magnitude = (code + 2) >> 1;
      values[i] = (code & 1) == 0 ? magnitude : -magnitude;
      activity += magnitude;

      if (suffixLength == 0) suffixLength = 1;

      var threshold = 3 << (suffixLength - 1);
      if (suffixLength < 6 && magnitude > threshold)
        suffixLength++;
    }

    return activity;
  }

  private int ReadTotalZeros(int total, bool chromaDirect)
  {
    if (chromaDirect)
    {
      var width = CavlcTables.TotalZerosChromaDcBitWidths[total - 1];
      var entry = CavlcTables.TotalZerosChromaDc[total - 1][_bits.Peek(width)];
      _bits.Skip(entry.Length);
      return entry.Zeros;
    }

    var lumaWidth = CavlcTables.TotalZeros4x4BitWidths[total - 1];
    var lumaEntry = CavlcTables.TotalZeros4x4[total - 1][_bits.Peek(lumaWidth)];
    _bits.Skip(lumaEntry.Length);
    return lumaEntry.Zeros;
  }

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

      var width = CavlcTables.RunBeforeBitWidths[Math.Min(zerosLeft, 7) - 1];
      var value = _bits.Peek(width);

      if (zerosLeft < 7)
      {
        var entry = CavlcTables.RunBefore[zerosLeft - 1][value];
        _bits.Skip(entry.Length);
        runs[i] = entry.Run;
      }
      else
      {
        _bits.Skip(width);
        var entry = CavlcTables.RunBefore[6][value];
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

  private void SkipRuns(int total, int zerosLeft)
  {
    for (var i = 0; i < total - 1 && zerosLeft > 0; i++)
    {
      var width = CavlcTables.RunBeforeBitWidths[Math.Min(zerosLeft, 7) - 1];
      var value = _bits.Peek(width);

      int run;
      if (zerosLeft < 7)
      {
        var entry = CavlcTables.RunBefore[zerosLeft - 1][value];
        _bits.Skip(entry.Length);
        run = entry.Run;
      }
      else
      {
        _bits.Skip(width);
        var entry = CavlcTables.RunBefore[6][value];
        if (entry.Run < 7)
        {
          run = entry.Run;
        }
        else
        {
          var prefix = _bits.PrefixLength();
          run = prefix + 6;
          _bits.Skip(prefix);
        }
      }

      zerosLeft -= run;
    }
  }
}
