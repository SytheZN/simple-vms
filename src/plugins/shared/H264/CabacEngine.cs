using System.Numerics;

namespace H264;

public class CabacEngine
{
  private const int SliceQpMax = 51;
  private const int PreCtxStateMin = 1;
  private const int PreCtxStateMax = 126;
  private const int PreCtxStateMpsThreshold = 63;
  private const int PreCtxStateMpsShift = 64;

  private const int InitialRange = 510;
  private const int OffsetPrimingBits = 9;
  private const int OffsetPrimingFillBits = 32;
  private const int BitsPerByte = 8;

  private static readonly uint[] RangeLpsRows = PackRangeLps();

  private static uint[] PackRangeLps()
  {
    var rows = new uint[64];
    for (var state = 0; state < 64; state++)
    {
      uint row = 0;
      for (var quarter = 0; quarter < 4; quarter++)
        row |= (uint)CabacArithmeticTables.RangeTabLps[state, quarter] << (quarter << 3);
      rows[state] = row;
    }
    return rows;
  }

  private static ulong Pack(byte context) =>
    RangeLpsRows[context >> 1] | ((ulong)context << 32);

  private static readonly ulong[] Transition = PackTransitions();

  private static ulong[] PackTransitions()
  {
    var packed = new ulong[256];

    for (var context = 0; context < 128; context++)
    {
      var state = context >> 1;
      var mps = context & 1;

      packed[context << 1] =
        Pack((byte)((CabacArithmeticTables.TransIdxMps[state] << 1) | mps));
      packed[(context << 1) | 1] =
        Pack((byte)((CabacArithmeticTables.TransIdxLps[state] << 1)
          | (state == 0 ? 1 - mps : mps)));
    }

    return packed;
  }

  private byte[] _data = [];
  private readonly ulong[] _contexts;
  private int _length;

  private long _low;
  private int _bits;
  private int _bytePos;
  private int _range;

  public int BytesRead => Math.Min(_length, (_bytePos * BitsPerByte - _bits) / BitsPerByte);
  public int BytesTotal => _length;

  public CabacEngine() : this(CabacContextInitTables.CtxCount) { }

  protected CabacEngine(int contextCount) => _contexts = new ulong[contextCount];

  public void Initialize(
    byte[] rbsp, int length, int bitOffset, int sliceQp, CabacInitType initType)
  {
    _data = rbsp;
    _length = length;
    _bytePos = (bitOffset + BitsPerByte - 1) / BitsPerByte;
    _low = 0;
    _bits = 0;

    InitContexts(Math.Clamp(sliceQp, 0, SliceQpMax), initType);
    Start();
  }

  protected virtual void InitContexts(int sliceQp, CabacInitType initType)
  {
    var m = CabacContextInitTables.InitM[(int)initType];
    var n = CabacContextInitTables.InitN[(int)initType];

    for (var i = 0; i < _contexts.Length; i++)
      SetContext(i, m[i], n[i], sliceQp);
  }

  protected void SetContext(int index, int m, int n, int sliceQp)
  {
    var preCtxState = Math.Clamp(((m * sliceQp) >> 4) + n, PreCtxStateMin, PreCtxStateMax);
    _contexts[index] = Pack(preCtxState <= PreCtxStateMpsThreshold
      ? (byte)((PreCtxStateMpsThreshold - preCtxState) << 1)
      : (byte)(((preCtxState - PreCtxStateMpsShift) << 1) | 1));
  }

  private void Start()
  {
    _range = InitialRange;
    Fill(OffsetPrimingFillBits);
    _bits -= OffsetPrimingBits;
  }

  public int DecodeDecision(int ctxIdx)
  {
    var packed = _contexts[ctxIdx];
    var rangeLps = (int)((packed >> ((_range >> 3) & 24)) & 0xFF);
    var rangeMps = _range - rangeLps;
    var scaled = (long)rangeMps << _bits;

    var mps = (_low - scaled) >> 63;
    var lps = (int)(~mps & 1);

    _low -= scaled & ~mps;
    _range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

    var context = (int)(packed >> 32);
    _contexts[ctxIdx] = Transition[(context << 1) | lps];

    Renormalize();
    return (context & 1) ^ lps;
  }

  public int DecodeDecisionRun(
    int ctxBase, ReadOnlySpan<byte> offsets, int from, Span<byte> positions, int count)
  {
    var low = _low;
    var bits = _bits;
    var range = _range;
    var bytePos = _bytePos;
    var data = _data;
    var length = _length;
    var contexts = _contexts;

    for (var n = from; n > 0; n--)
    {
      var ctxIdx = ctxBase + offsets[n];
      var packed = contexts[ctxIdx];
      var rangeLps = (int)((packed >> ((range >> 3) & 24)) & 0xFF);
      var rangeMps = range - rangeLps;
      var scaled = (long)rangeMps << bits;

      var mps = (low - scaled) >> 63;
      var lps = (int)(~mps & 1);

      low -= scaled & ~mps;
      range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

      var context = (int)(packed >> 32);
      contexts[ctxIdx] = Transition[(context << 1) | lps];

      positions[count] = (byte)n;
      count += (context & 1) ^ lps;

      if (bits < 8)
      {
        if (bytePos + 4 <= length)
        {
          low = (low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(bytePos));
          bytePos += 4;
          bits += 32;
        }
        else
        {
          while (bits < 32)
          {
            low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
            bytePos++;
            bits += 8;
          }
        }
      }

      var shift = BitOperations.LeadingZeroCount((uint)range) - 23;
      range <<= shift;
      bits -= shift;
    }

    _low = low;
    _bits = bits;
    _range = range;
    _bytePos = bytePos;
    return count;
  }

  public int DecodeSignificanceRun(
    int cbfCtx, int sigBase, int lastBase, ReadOnlySpan<byte> sigOffsets,
    ReadOnlySpan<byte> lastOffsets, int last, Span<byte> positions, out bool ended)
  {
    var low = _low;
    var bits = _bits;
    var range = _range;
    var bytePos = _bytePos;
    var data = _data;
    var length = _length;

    var contexts = _contexts;

    var count = 0;
    ended = false;

    if (cbfCtx >= 0)
    {
      var packed = contexts[cbfCtx];
      var rangeLps = (int)((packed >> ((range >> 3) & 24)) & 0xFF);
      var rangeMps = range - rangeLps;
      var scaled = (long)rangeMps << bits;

      var mps = (low - scaled) >> 63;
      var lps = (int)(~mps & 1);

      low -= scaled & ~mps;
      range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

      var context = (int)(packed >> 32);
      contexts[cbfCtx] = Transition[(context << 1) | lps];

      if (bits < 8)
      {
        if (bytePos + 4 <= length)
        {
          low = (low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(bytePos));
          bytePos += 4;
          bits += 32;
        }
        else
        {
          while (bits < 32)
          {
            low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
            bytePos++;
            bits += 8;
          }
        }
      }

      var shift = BitOperations.LeadingZeroCount((uint)range) - 23;
      range <<= shift;
      bits -= shift;

      if (((context & 1) ^ lps) == 0)
      {
        _low = low;
        _bits = bits;
        _range = range;
        _bytePos = bytePos;
        return -1;
      }
    }

    for (var n = 0; n < last; n++)
    {
      var ctxIdx = sigBase + sigOffsets[n];
      var packed = contexts[ctxIdx];
      var rangeLps = (int)((packed >> ((range >> 3) & 24)) & 0xFF);
      var rangeMps = range - rangeLps;
      var scaled = (long)rangeMps << bits;

      var mps = (low - scaled) >> 63;
      var lps = (int)(~mps & 1);

      low -= scaled & ~mps;
      range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

      var context = (int)(packed >> 32);
      contexts[ctxIdx] = Transition[(context << 1) | lps];

      if (bits < 8)
      {
        if (bytePos + 4 <= length)
        {
          low = (low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(bytePos));
          bytePos += 4;
          bits += 32;
        }
        else
        {
          while (bits < 32)
          {
            low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
            bytePos++;
            bits += 8;
          }
        }
      }

      var shift = BitOperations.LeadingZeroCount((uint)range) - 23;
      range <<= shift;
      bits -= shift;

      if (((context & 1) ^ lps) == 0) continue;

      positions[count++] = (byte)n;

      ctxIdx = lastBase + lastOffsets[n];
      packed = contexts[ctxIdx];
      rangeLps = (int)((packed >> ((range >> 3) & 24)) & 0xFF);
      rangeMps = range - rangeLps;
      scaled = (long)rangeMps << bits;

      mps = (low - scaled) >> 63;
      lps = (int)(~mps & 1);

      low -= scaled & ~mps;
      range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

      context = (int)(packed >> 32);
      contexts[ctxIdx] = Transition[(context << 1) | lps];

      if (bits < 8)
      {
        if (bytePos + 4 <= length)
        {
          low = (low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(bytePos));
          bytePos += 4;
          bits += 32;
        }
        else
        {
          while (bits < 32)
          {
            low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
            bytePos++;
            bits += 8;
          }
        }
      }

      shift = BitOperations.LeadingZeroCount((uint)range) - 23;
      range <<= shift;
      bits -= shift;

      if (((context & 1) ^ lps) == 1)
      {
        ended = true;
        break;
      }
    }

    _low = low;
    _bits = bits;
    _range = range;
    _bytePos = bytePos;
    return count;
  }

  public int DecodeLevelRun(
    int oneBase, int absBase, int cap, int prefixLimit, int escapeLimit,
    Span<int> levels, int count)
  {
    var low = _low;
    var bits = _bits;
    var range = _range;
    var bytePos = _bytePos;
    var data = _data;
    var length = _length;

    var contexts = _contexts;

    var emit = !levels.IsEmpty;
    var sum = 0;
    var beyond = 0;
    var exactly = 0;

    for (var n = count - 1; n >= 0; n--)
    {
      var oneCtx = oneBase + (beyond != 0 ? 0 : Math.Min(4, 1 + exactly));

      var packed = contexts[oneCtx];
      var rangeLps = (int)((packed >> ((range >> 3) & 24)) & 0xFF);
      var rangeMps = range - rangeLps;
      var scaled = (long)rangeMps << bits;

      var mps = (low - scaled) >> 63;
      var lps = (int)(~mps & 1);

      low -= scaled & ~mps;
      range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

      var context = (int)(packed >> 32);
      contexts[oneCtx] = Transition[(context << 1) | lps];

      if (bits < 8)
      {
        if (bytePos + 4 <= length)
        {
          low = (low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(bytePos));
          bytePos += 4;
          bits += 32;
        }
        else
        {
          while (bits < 32)
          {
            low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
            bytePos++;
            bits += 8;
          }
        }
      }

      var shift = BitOperations.LeadingZeroCount((uint)range) - 23;
      range <<= shift;
      bits -= shift;

      var level = 1;

      if (((context & 1) ^ lps) == 1)
      {
        var absCtx = absBase + Math.Min(cap, beyond);
        var held = contexts[absCtx];
        var prefix = 1;

        while (prefix < prefixLimit)
        {
          rangeLps = (int)((held >> ((range >> 3) & 24)) & 0xFF);
          rangeMps = range - rangeLps;
          scaled = (long)rangeMps << bits;

          mps = (low - scaled) >> 63;
          lps = (int)(~mps & 1);

          low -= scaled & ~mps;
          range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

          context = (int)(held >> 32);
          held = Transition[(context << 1) | lps];

          if (bits < 8)
          {
            if (bytePos + 4 <= length)
            {
              low = (low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                data.AsSpan(bytePos));
              bytePos += 4;
              bits += 32;
            }
            else
            {
              while (bits < 32)
              {
                low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
                bytePos++;
                bits += 8;
              }
            }
          }

          shift = BitOperations.LeadingZeroCount((uint)range) - 23;
          range <<= shift;
          bits -= shift;

          if (((context & 1) ^ lps) == 0) break;
          prefix++;
        }

        contexts[absCtx] = held;
        level = prefix + 1;

        if (prefix == prefixLimit)
        {
          _low = low;
          _bits = bits;
          _range = range;
          _bytePos = bytePos;

          level += DecodeBypassExpGolomb(escapeLimit);

          low = _low;
          bits = _bits;
          range = _range;
          bytePos = _bytePos;
        }

        beyond++;
      }
      else
      {
        exactly++;
      }

      if (bits < 1)
      {
        if (bytePos + 4 <= length)
        {
          low = (low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(bytePos));
          bytePos += 4;
          bits += 32;
        }
        else
        {
          while (bits < 32)
          {
            low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
            bytePos++;
            bits += 8;
          }
        }
      }

      bits--;
      scaled = (long)range << bits;
      var below = (low - scaled) >> 63;
      low -= scaled & ~below;

      sum += level;
      if (emit) levels[n] = below < 0 ? level : -level;
    }

    _low = low;
    _bits = bits;
    _range = range;
    _bytePos = bytePos;
    return sum;
  }

  public int DecodeFlagOrField(int flagCtx, int fieldCtx, int width)
  {
    var low = _low;
    var bits = _bits;
    var range = _range;
    var bytePos = _bytePos;
    var data = _data;
    var length = _length;
    var contexts = _contexts;

    var field = 0;

    var at = -1;
    var held = contexts[flagCtx];

    while (true)
    {
      var rangeLps = (int)((held >> ((range >> 3) & 24)) & 0xFF);
      var rangeMps = range - rangeLps;
      var scaled = (long)rangeMps << bits;

      var mps = (low - scaled) >> 63;
      var lps = (int)(~mps & 1);

      low -= scaled & ~mps;
      range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

      var context = (int)(held >> 32);
      held = Transition[(context << 1) | lps];

      if (bits < 8)
      {
        if (bytePos + 4 <= length)
        {
          low = (low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(bytePos));
          bytePos += 4;
          bits += 32;
        }
        else
        {
          while (bits < 32)
          {
            low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
            bytePos++;
            bits += 8;
          }
        }
      }

      var shift = BitOperations.LeadingZeroCount((uint)range) - 23;
      range <<= shift;
      bits -= shift;

      var bin = (context & 1) ^ lps;

      if (at < 0)
      {
        contexts[flagCtx] = held;

        if (bin == 1)
        {
          field = -1;
          break;
        }

        held = contexts[fieldCtx];
        at = 0;
        continue;
      }

      field |= bin << at;
      if (++at == width)
      {
        contexts[fieldCtx] = held;
        break;
      }
    }

    _low = low;
    _bits = bits;
    _range = range;
    _bytePos = bytePos;
    return field;
  }

  public int DecodeBypassExpGolomb(int limit)
  {
    var width = DecodeBypassUnary(limit);
    if (width == 0) return 0;

    return (int)(DecodeBypassBits(width) + (1u << width) - 1);
  }

  public int DecodeBypass()
  {
    if (_bits < 1) Fill(32);
    _bits--;

    var scaled = (long)_range << _bits;

    var below = (_low - scaled) >> 63;
    _low -= scaled & ~below;
    return (int)(~below & 1);
  }

  public int DecodeBypassUnary(int limit)
  {
    if (_bits < limit) Fill(limit + 8);

    var low = _low;
    var bits = _bits;
    var count = 0;

    while (count < limit)
    {
      bits--;
      var scaled = (long)_range << bits;
      if (low < scaled) break;
      low -= scaled;
      count++;
    }

    _low = low;
    _bits = bits;
    return count;
  }

  public int DecodeBypassRice(int rice, int limit)
  {
    if (_bits < limit) Fill(limit + 8);

    var low = _low;
    var bits = _bits;
    var prefix = 0;

    while (prefix < limit)
    {
      bits--;
      var scaled = (long)_range << bits;
      if (low < scaled) break;
      low -= scaled;
      prefix++;
    }

    _low = low;
    _bits = bits;

    if (prefix >= limit) return -1;

    var suffix = prefix <= 3 ? rice : prefix - 3 + rice;
    var value = prefix <= 3 ? prefix << rice : ((1 << (prefix - 3)) + 2) << rice;

    return value + (int)DecodeBypassBits(suffix);
  }

  public uint DecodeBypassBits(int count)
  {
    uint value = 0;
    while (count > MaxBypassBatch)
    {
      value = (value << MaxBypassBatch) | DecodeBypassBatch(MaxBypassBatch);
      count -= MaxBypassBatch;
    }

    return (value << count) | DecodeBypassBatch(count);
  }

  private const int MaxBypassBatch = 16;

  private const int ReciprocalShift = 40;

  private static readonly ulong[] RangeReciprocal = BuildReciprocals();

  private static ulong[] BuildReciprocals()
  {
    var table = new ulong[512];
    for (var range = 1; range < table.Length; range++)
      table[range] = (ulong)((1L << ReciprocalShift) / range) + 1;
    return table;
  }

  private uint DecodeBypassBatch(int count)
  {
    if (count == 0) return 0;
    if (_bits < count) Fill(count + 16);

    _bits -= count;
    var above = (ulong)(_low >> _bits);
    var bins = (uint)((above * RangeReciprocal[_range]) >> ReciprocalShift);
    _low -= (long)bins * ((long)_range << _bits);
    return bins;
  }

  public int Suspend() => _bytePos - (_bits >> 3);

  public void Resume(int bytePos)
  {
    _bytePos = bytePos;
    _low = 0;
    _bits = 0;
    Start();
  }

  public int DecodeTerminate()
  {
    _range -= 2;
    if (_low >= (long)_range << _bits)
      return 1;

    Renormalize();
    return 0;
  }

  private void Renormalize()
  {
    if (_bits < 8) Fill(32);

    var shift = BitOperations.LeadingZeroCount((uint)_range) - 23;
    _range <<= shift;
    _bits -= shift;
  }

  private void Fill(int need)
  {
    if (_bits <= 16 && _bytePos + 4 <= _length)
    {
      _low = (_low << 32) | System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
        _data.AsSpan(_bytePos));
      _bytePos += 4;
      _bits += 32;
    }

    while (_bits < need)
    {
      var next = _bytePos < _length ? _data[_bytePos] : (byte)0;
      _bytePos++;
      _low = (_low << 8) | next;
      _bits += 8;
    }
  }
}
