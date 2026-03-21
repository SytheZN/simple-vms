namespace Format.Fmp4;

public sealed class TimestampConverter
{
  private readonly uint _timescale;
  private uint _baseRtp;
  private uint _prevRtp;
  private ulong _wrapOffset;
  private bool _initialized;

  public TimestampConverter(uint timescale = 90000)
  {
    _timescale = timescale;
  }

  public uint Timescale => _timescale;

  public ulong ToDecodeTime(ulong timestamp)
  {
    var rtp32 = (uint)timestamp;

    if (!_initialized)
    {
      _baseRtp = rtp32;
      _prevRtp = rtp32;
      _wrapOffset = 0;
      _initialized = true;
      return 0;
    }

    if (rtp32 < _prevRtp && (_prevRtp - rtp32) > 0x80000000u)
      _wrapOffset += 0x1_0000_0000UL;

    _prevRtp = rtp32;
    return _wrapOffset + rtp32 - _baseRtp;
  }

  public uint DurationBetween(ulong from, ulong to)
  {
    return (uint)(to - from);
  }

  public void Reset()
  {
    _initialized = false;
    _baseRtp = 0;
    _prevRtp = 0;
    _wrapOffset = 0;
  }
}
