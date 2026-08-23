using System.Numerics;
using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

/// <summary>
/// Arithmetic decoder shared by H.264 and H.265, which use the same
/// state machine and the same transition tables. Only context initialisation differs: H.264 reads
/// an m/n pair per context, H.265 derives them from a single packed byte.
///
/// The offset and the bits waiting to enter it are one register rather than two. Holding the offset
/// already scaled by the bits behind it means renormalisation only has to say how many of them the
/// range has caught up with, so no bin moves bits from a buffer into an offset, and the stream is
/// touched once every few dozen bins instead of once per bin.
/// </summary>
internal sealed class CabacEngine
{
  /// <summary>
  /// The upstream table is two-dimensional and a state's four quarters are one word wide, so each
  /// state's row is held whole. Which quarter a bin wants is the one thing that depends on the
  /// range, and the range is what every bin waits on - fetching the row by state alone keeps the
  /// load off that chain and leaves a shift to pick the byte out once the range is known.
  /// </summary>
  private static readonly uint[] RangeLpsRows = PackRangeLps();

  private static uint[] PackRangeLps()
  {
    var rows = new uint[64];
    for (var state = 0; state < 64; state++)
    {
      uint row = 0;
      for (var quarter = 0; quarter < 4; quarter++)
        row |= (uint)H264CabacArithmeticTables.RangeTabLps[state, quarter] << (quarter << 3);
      rows[state] = row;
    }
    return rows;
  }

  /// <summary>
  /// A context's state decides which four range values it can take, and the range decides which one
  /// of them this bin gets. Only the second of those is known late, so a context carries its whole
  /// row with it: bits 0 to 31 are the four values, bits 32 to 38 the state and MPS it came from.
  /// A decision then reaches the range with one load where it used to need two, one to learn the
  /// state and another to look up what that state permits.
  /// </summary>
  private static ulong Pack(byte context) =>
    RangeLpsRows[context >> 1] | ((ulong)context << 32);

  /// <summary>
  /// Both outcomes of every context, pre-packed and indexed by the outcome's own bit, so a decision
  /// stores one value without branching on which outcome it was.
  /// </summary>
  private static readonly ulong[] Transition = PackTransitions();

  private static ulong[] PackTransitions()
  {
    var packed = new ulong[256];

    for (var context = 0; context < 128; context++)
    {
      var state = context >> 1;
      var mps = context & 1;

      packed[context << 1] =
        Pack((byte)((H264CabacArithmeticTables.TransIdxMps[state] << 1) | mps));
      packed[(context << 1) | 1] =
        Pack((byte)((H264CabacArithmeticTables.TransIdxLps[state] << 1)
          | (state == 0 ? 1 - mps : mps)));
    }

    return packed;
  }

  private byte[] _data = [];
  private ulong[] _contexts = [];
  private int _length;

  /// <summary>The offset, scaled up by the <see cref="_bits"/> stream bits held behind it.</summary>
  private long _low;
  private int _bits;
  private int _bytePos;
  private int _range;

  public int BytesRead => Math.Min(_length, (_bytePos * 8 - _bits) >> 3);
  public int BytesTotal => _length;

  /// <summary>
  /// One engine is reset per picture rather than constructed, so the context arrays outlive the
  /// slice. <paramref name="length"/> says how much of <paramref name="rbsp"/> is this picture,
  /// which lets the caller keep a buffer larger than the current one.
  /// </summary>
  private void Prepare(byte[] rbsp, int length, int bitOffset, int contextCount)
  {
    _data = rbsp;
    _length = length;
    if (_contexts.Length != contextCount)
      _contexts = new ulong[contextCount];
    _bytePos = (bitOffset + 7) >> 3;
    _low = 0;
    _bits = 0;
  }

  public void ResetForH264(byte[] rbsp, int length, int bitOffset, int sliceQp)
  {
    Prepare(rbsp, length, bitOffset, H264CabacContextInitTables.CtxCount);
    var m = H264CabacContextInitTables.InitM[0];
    var n = H264CabacContextInitTables.InitN[0];
    var qp = Math.Clamp(sliceQp, 0, 51);

    for (var i = 0; i < _contexts.Length; i++)
      SetContext(i, m[i], n[i], qp);

    Start();
  }

  public void ResetForH265(byte[] rbsp, int length, int bitOffset, int sliceQp)
  {
    Prepare(rbsp, length, bitOffset, H265CabacContextInitTables.CtxCount);
    var init = H265CabacContextInitTables.InitValue[0];
    var qp = Math.Clamp(sliceQp, 0, 51);

    for (var i = 0; i < _contexts.Length; i++)
    {
      var m = (init[i] >> 4) * 5 - 45;
      var n = ((init[i] & 15) << 3) - 16;
      SetContext(i, m, n, qp);
    }

    Start();
  }

  /// <summary>The quantiser arrives already clamped; every context would otherwise reclamp it.</summary>
  private void SetContext(int index, int m, int n, int sliceQp)
  {
    var preCtxState = Math.Clamp(((m * sliceQp) >> 4) + n, 1, 126);
    _contexts[index] = Pack(preCtxState <= 63
      ? (byte)((63 - preCtxState) << 1)
      : (byte)(((preCtxState - 64) << 1) | 1));
  }

  /// <summary>
  /// The first nine bits are the offset, and in this form taking them is only a matter of saying so:
  /// they are already the top of the register, and the rest stay behind them as lookahead.
  /// </summary>
  private void Start()
  {
    _range = 510;
    Fill(32);
    _bits -= 9;
  }

  /// <summary>
  /// Which subinterval the offset falls in is what arithmetic coding makes unpredictable - a branch
  /// here mispredicts by design, so both outcomes are computed and selected with a mask instead.
  /// </summary>
  public int DecodeDecision(int ctxIdx)
  {
    var packed = _contexts[ctxIdx];
    var rangeLps = (int)((packed >> ((_range >> 3) & 24)) & 0xFF);
    var rangeMps = _range - rangeLps;
    var scaled = (long)rangeMps << _bits;

    // All ones while the offset stays inside the most probable subinterval.
    var mps = (_low - scaled) >> 63;
    var lps = (int)(~mps & 1);

    _low -= scaled & ~mps;
    _range = (rangeMps & (int)mps) | (rangeLps & ~(int)mps);

    var context = (int)(packed >> 32);
    _contexts[ctxIdx] = Transition[(context << 1) | lps];

    Renormalize();
    return (context & 1) ^ lps;
  }

  /// <summary>
  /// Decodes a run of decisions whose context offsets are all settled before the run starts, and
  /// records the position of every bin that comes back one. A significance walk is the case: which
  /// context a flag uses depends on where the coefficient sits, never on what an earlier flag
  /// decoded, so the whole run can be walked without stopping. <paramref name="offsets"/> is read
  /// by scan position, so nothing here waits on a table to say where in the block that is.
  ///
  /// The arithmetic is <see cref="DecodeDecision"/>'s, spelled out again rather than called: the
  /// point of the run is that the range, the offset and the bit count stay in registers across all
  /// of it. Through the single-bin entry point they go back to fields between every bin, and the
  /// offset's store and reload land on the dependency chain that already decides how fast a bin can
  /// be decoded at all.
  /// </summary>
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
        while (bits < 32)
        {
          low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
          bytePos++;
          bits += 8;
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

  /// <summary>
  /// H.264's significance walk, terminator interleaved. Returns how many positions were written, or
  /// -1 when <paramref name="cbfCtx"/> said the block carries none; the caller owes the final
  /// position to a run that ends without <paramref name="ended"/>.
  /// </summary>
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
        while (bits < 32)
        {
          low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
          bytePos++;
          bits += 8;
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
        while (bits < 32)
        {
          low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
          bytePos++;
          bits += 8;
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
        while (bits < 32)
        {
          low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
          bytePos++;
          bits += 8;
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

  /// <summary>
  /// Every coefficient's magnitude and sign for one block, decoded backwards along the scan. The
  /// escape spills to the fields rather than staying in registers: only levels past fifteen reach
  /// it, and it is a whole coding tree of its own.
  /// </summary>
  public void DecodeLevelRun(
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
        while (bits < 32)
        {
          low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
          bytePos++;
          bits += 8;
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
            while (bits < 32)
            {
              low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
              bytePos++;
              bits += 8;
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
        while (bits < 32)
        {
          low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
          bytePos++;
          bits += 8;
        }

      bits--;
      scaled = (long)range << bits;
      var below = (low - scaled) >> 63;
      low -= scaled & ~below;

      levels[n] = below < 0 ? level : -level;
    }

    _low = low;
    _bits = bits;
    _range = range;
    _bytePos = bytePos;
  }

  /// <summary>
  /// A flag on one context and, when it is clear, a fixed-width field on a second, lowest bin first.
  /// Returns -1 when the flag is set and the field is therefore absent.
  /// </summary>
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

    // Negative while the flag itself is being read, and the field's bit position after that.
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
        while (bits < 32)
        {
          low = (low << 8) | (bytePos < length ? data[bytePos] : (byte)0);
          bytePos++;
          bits += 8;
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

  /// <summary>
  /// What a level past its unary prefix carries: a run of ones widening the field that follows, then
  /// the field itself. Both halves are bypass, so the run is walked on a copy of the register and the
  /// field comes out in one batch rather than a bin at a time.
  /// </summary>
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

    // All ones while the offset stays below the range, which is the zero bin.
    var below = (_low - scaled) >> 63;
    _low -= scaled & ~below;
    return (int)(~below & 1);
  }

  /// <summary>
  /// Counts leading one bins, stopping at the first zero or at <paramref name="limit"/>. A unary
  /// prefix cannot be batched the way a fixed-width field can, because reading past its terminator
  /// would consume bins the next element owns. But the bins are a function of the register, so the
  /// walk runs on a copy of it and only the bins actually spent are taken.
  /// </summary>
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

  /// <summary>
  /// A Golomb-Rice code: a unary prefix, then a fixed field whose width the prefix decides. Both
  /// halves are bypass and both come out of the same register, so taking them together spares an
  /// entry into the engine and the state that would go back to fields between them - which is worth
  /// having on a code every coefficient past its threshold pays.
  ///
  /// Returns -1 when the prefix runs past <paramref name="limit"/>, meaning CABAC has lost sync.
  /// </summary>
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

    // Past the third prefix the code switches from one value per step to a doubling range, which is
    // what the suffix widens to carry.
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

  /// <summary>Bounded so the shifted range stays well inside a long.</summary>
  private const int MaxBypassBatch = 16;

  private const int ReciprocalShift = 40;

  /// <summary>
  /// A reciprocal of every range the decoder can hold, so the long division a bypass batch is takes
  /// a multiply instead. The range is nine bits and a batch at most sixteen, so the quotient and the
  /// range multiply out to thirty-four - well inside what this reciprocal is exact over, which is
  /// what lets it stand in for the division rather than approximate it.
  /// </summary>
  private static readonly ulong[] RangeReciprocal = BuildReciprocals();

  private static ulong[] BuildReciprocals()
  {
    var table = new ulong[512];
    for (var range = 1; range < table.Length; range++)
      table[range] = (ulong)((1L << ReciprocalShift) / range) + 1;
    return table;
  }

  /// <summary>
  /// Doubling a remainder, comparing it against a fixed divisor and subtracting when it fits is
  /// long division, and the range is fixed across a bypass run - so the bins are the quotient of
  /// the offset by the range at the scale the run ends on, and the offset keeps the remainder.
  ///
  /// Dividing by the scaled range is dividing by the range and then by a power of two, and the
  /// power of two is a shift - which leaves a nine-bit divisor, small enough to have its reciprocal
  /// waiting in a table.
  /// </summary>
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

  /// <summary>
  /// Where raw samples begin, for the one macroblock kind that carries them uncoded. The engine
  /// reads ahead, so the stream has only really been consumed as far as the whole bytes still
  /// sitting behind the offset allow - and the bits past that byte are the alignment padding,
  /// which is discarded rather than examined.
  /// </summary>
  public int Suspend() => _bytePos - (_bits >> 3);

  /// <summary>
  /// Picks the stream back up at a byte boundary. Only the arithmetic decoder restarts: the
  /// context states carry across, since raw samples teach them nothing but do not unlearn what
  /// the macroblocks before them did.
  /// </summary>
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

  /// <summary>
  /// The range is at most nine bits, so the leading zero count says directly how far it is from the
  /// 256 the decoder renormalises up to - and scaling the range up by that much is the same as
  /// saying the offset holds that many fewer bits behind it.
  /// </summary>
  private void Renormalize()
  {
    if (_bits < 8) Fill(32);

    var shift = BitOperations.LeadingZeroCount((uint)_range) - 23;
    _range <<= shift;
    _bits -= shift;
  }

  /// <summary>
  /// Reads past the end of the slice as zeros. A truncated NAL then decodes as garbage rather
  /// than throwing, which suits a preview: a partial image beats no image.
  /// </summary>
  private void Fill(int need)
  {
    while (_bits < need)
    {
      var next = _bytePos < _length ? _data[_bytePos] : (byte)0;
      _bytePos++;
      _low = (_low << 8) | next;
      _bits += 8;
    }
  }
}
