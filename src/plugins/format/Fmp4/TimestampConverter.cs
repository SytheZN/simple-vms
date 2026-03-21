namespace Format.Fmp4;

public sealed class TimestampConverter
{
  private readonly uint _timescale;
  private ulong _baseTimestamp;
  private bool _initialized;

  public TimestampConverter(uint timescale = 90000)
  {
    _timescale = timescale;
  }

  public uint Timescale => _timescale;

  public ulong ToDecodeTime(ulong timestamp)
  {
    if (!_initialized)
    {
      _baseTimestamp = timestamp;
      _initialized = true;
    }

    return timestamp - _baseTimestamp;
  }

  public uint DurationBetween(ulong from, ulong to)
  {
    return (uint)(to - from);
  }

  public void Reset()
  {
    _initialized = false;
    _baseTimestamp = 0;
  }
}
